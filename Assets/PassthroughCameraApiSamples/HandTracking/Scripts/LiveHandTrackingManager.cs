using System;
using System.Threading.Tasks;
using Unity.Mathematics;
using Unity.InferenceEngine;
using UnityEngine;

// 두 예제의 핵심 로직을 결합한 메인 컨트롤러
namespace PassthroughCameraSamples.HandTracking
{
    [MetaCodeSample("PassthroughCameraApiSamples-HandTracking")]
    public class LiveHandTrackingManager : MonoBehaviour
    {
        [Header("Dependencies")]
        [SerializeField] private PassthroughTextureProvider m_textureProvider;
        [SerializeField] private EnvironmentRayCastSampleManager m_environmentRaycast;
        [SerializeField] private HandPreview m_handPreview;

        [Header("Model Assets")]
        [SerializeField] private ModelAsset m_handDetectorModelAsset;
        [SerializeField] private ModelAsset m_handLandmarkerModelAsset;
        [SerializeField] private TextAsset m_anchorsCsv;

        [Header("Detection Settings")]
        [SerializeField, Range(0f, 1f)] private float m_scoreThreshold = 0.65f;

        // 모델 관련 상수
        private const int k_NumAnchors = 2016;
        private const int k_NumKeypoints = 21;
        private const int k_DetectorInputSize = 192;
        private const int k_LandmarkerInputSize = 224;

        // Inference Engine 관련 변수
        private Worker m_detectorWorker;
        private Worker m_landmarkerWorker;
        private Tensor<float> m_detectorInput;
        private Tensor<float> m_landmarkerInput;
        private float[,] m_anchors;

        private bool m_isTracking = false;

        async void Start()
        {
            // 1. 의존성 및 권한 확인
            if (m_textureProvider == null || m_environmentRaycast == null || m_handPreview == null)
            {
                Debug.LogError("모든 의존성(Provider, Raycast, Preview)을 Inspector에서 설정해주세요.");
                return;
            }

            // 2. 모델 및 앵커 로드
            m_anchors = BlazeUtils.LoadAnchors(m_anchorsCsv.text, k_NumAnchors);
            var handDetectorModel = ModelLoader.Load(m_handDetectorModelAsset);

            // 3. 탐지 모델 최적화 (가장 점수 높은 손만 필터링하도록 후처리)
            var graph = new FunctionalGraph();
            var input = graph.AddInput(handDetectorModel, 0);
            var outputs = Functional.Forward(handDetectorModel, input);
            var (idx, scores, boxes) = BlazeUtils.ArgMaxFiltering(outputs[0], outputs[1]);
            handDetectorModel = graph.Compile(idx, scores, boxes);

            // 4. Inference Engine Worker 생성
            m_detectorWorker = new Worker(handDetectorModel, BackendType.GPUCompute);
            var handLandmarkerModel = ModelLoader.Load(m_handLandmarkerModelAsset);
            m_landmarkerWorker = new Worker(handLandmarkerModel, BackendType.GPUCompute);

            // 5. 입력 텐서 초기화
            m_detectorInput = new Tensor<float>(new TensorShape(1, k_DetectorInputSize, k_DetectorInputSize, 3));
            m_landmarkerInput = new Tensor<float>(new TensorShape(1, k_LandmarkerInputSize, k_LandmarkerInputSize, 3));

            Debug.Log("초기화 완료. 손 추적을 시작합니다.");
            m_isTracking = true;

            // 6. 메인 트래킹 루프 시작
            await HandTrackingLoop();
        }

        private async Task HandTrackingLoop()
        {
            while (m_isTracking)
            {
                // Passthrough 카메라 텍스처가 준비될 때까지 대기
                if (m_textureProvider.WebCamTexture == null)
                {
                    m_handPreview.SetActive(false);
                    await Task.Yield();
                    continue;
                }

                try
                {
                    await DetectAndTrackHand(m_textureProvider.WebCamTexture);
                }
                catch (Exception e)
                {
                    Debug.LogError($"추적 중 오류 발생: {e.Message}");
                    // 루프가 중단되지 않도록 오류 처리
                    m_handPreview.SetActive(false);
                    await Task.Delay(1000); // 오류 발생 시 잠시 대기
                }
            }
        }

        private async Task DetectAndTrackHand(Texture texture)
        {
            // --- 1단계: 손 탐지 (Hand Detection) ---

            var texWidth = texture.width;
            var texHeight = texture.height;
            var size = Mathf.Max(texWidth, texHeight);

            // 아핀 변환 행렬 계산 (카메라 이미지 -> 192x192 텐서)
            var scale = size / (float)k_DetectorInputSize;
            var M = BlazeUtils.mul(
                BlazeUtils.TranslationMatrix(0.5f * (new float2(texWidth, texHeight) + new float2(-size, size))),
                BlazeUtils.ScaleMatrix(new float2(scale, -scale))
            );
            BlazeUtils.SampleImageAffine(texture, m_detectorInput, M);

            // 탐지기 실행 및 결과 비동기적으로 받기
            m_detectorWorker.Schedule(m_detectorInput);
            using var outputIdx = await (m_detectorWorker.PeekOutput(0) as Tensor<int>).ReadbackAndCloneAsync();
            using var outputScore = await (m_detectorWorker.PeekOutput(1) as Tensor<float>).ReadbackAndCloneAsync();
            using var outputBox = await (m_detectorWorker.PeekOutput(2) as Tensor<float>).ReadbackAndCloneAsync();

            // 점수가 임계값보다 낮으면 손이 없는 것으로 간주
            if (outputScore[0] < m_scoreThreshold)
            {
                m_handPreview.SetActive(false);
                return;
            }

            // --- 2단계: 랜드마크 추정 (Landmark Estimation) ---

            // 탐지된 손의 위치, 크기, 회전 계산
            var idx = outputIdx[0];
            var anchorPos = k_DetectorInputSize * new float2(m_anchors[idx, 0], m_anchors[idx, 1]);
            var boxCenterTensor = anchorPos + new float2(outputBox[0, 0, 0], outputBox[0, 0, 1]);
            var boxSizeTensor = math.max(outputBox[0, 0, 2], outputBox[0, 0, 3]);

            var kp0 = anchorPos + new float2(outputBox[0, 0, 4], outputBox[0, 0, 5]); // 손목
            var kp2 = anchorPos + new float2(outputBox[0, 0, 8], outputBox[0, 0, 9]); // 중지 뿌리
            var delta = kp2 - kp0;
            var theta = math.atan2(delta.y, delta.x);
            var rotation = 0.5f * Mathf.PI - theta;
            boxCenterTensor += 0.5f * boxSizeTensor * (delta / math.length(delta));
            boxSizeTensor *= 2.6f;

            // 랜드마크 추정을 위한 두 번째 아핀 변환 행렬 계산
            var origin2 = new float2(0.5f * k_LandmarkerInputSize, 0.5f * k_LandmarkerInputSize);
            var scale2 = boxSizeTensor / k_LandmarkerInputSize;
            var M2 = BlazeUtils.mul(M, BlazeUtils.mul(BlazeUtils.mul(BlazeUtils.mul(
                BlazeUtils.TranslationMatrix(boxCenterTensor),
                BlazeUtils.ScaleMatrix(new float2(scale2, -scale2))),
                BlazeUtils.RotationMatrix(rotation)),
                BlazeUtils.TranslationMatrix(-origin2))
            );

            // 랜드마크 모델 입력 이미지 생성
            BlazeUtils.SampleImageAffine(texture, m_landmarkerInput, M2);

            // 랜드마크 추정기 실행
            m_landmarkerWorker.Schedule(m_landmarkerInput);
            using var landmarks = await (m_landmarkerWorker.PeekOutput("Identity") as Tensor<float>).ReadbackAndCloneAsync();

            // --- 3단계: 3D 시각화 (Visualization in 3D Space) ---
            m_handPreview.SetActive(true);

            var intrinsics = PassthroughCameraUtils.GetCameraIntrinsics(m_textureProvider.Eye);
            var camRes = intrinsics.Resolution;

            for (var i = 0; i < k_NumKeypoints; i++)
            {
                // 랜드마크의 2D 좌표 (224x224 텐서 공간)
                var landmarkCoordTensor = new float2(landmarks[3 * i], landmarks[3 * i + 1]);

                // 2D 좌표를 원본 카메라 이미지의 픽셀 좌표로 변환
                var positionImageSpace = BlazeUtils.mul(M2, landmarkCoordTensor);

                // 픽셀 좌표를 0~1 비율로 변환
                var perX = positionImageSpace.x / texWidth;
                var perY = positionImageSpace.y / texHeight;

                // Passthrough 카메라 해상도에 맞는 픽셀 좌표로 다시 계산
                var centerPixel = new Vector2Int(Mathf.RoundToInt(perX * camRes.x), Mathf.RoundToInt((1.0f - perY) * camRes.y));

                // 2D 픽셀 좌표에서 3D 공간으로 Ray 발사
                var ray = PassthroughCameraUtils.ScreenPointToRayInWorld(m_textureProvider.Eye, centerPixel);
                var worldPos = m_environmentRaycast.PlaceGameObjectByScreenPos(ray);

                // Raycast가 성공하면 해당 위치에 키포인트 렌더링
                if (worldPos.HasValue)
                {
                    m_handPreview.SetKeypoint(i, true, worldPos.Value);
                }
                else
                {
                    // Raycast 실패 시 키포인트를 비활성화하거나 대체 로직 사용
                    // 여기서는 간단히 비활성화
                    m_handPreview.SetKeypoint(i, false, Vector3.zero);
                }
            }
        }

        private void OnDestroy()
        {
            m_isTracking = false;
            m_detectorWorker?.Dispose();
            m_landmarkerWorker?.Dispose();
            m_detectorInput?.Dispose();
            m_landmarkerInput?.Dispose();
        }
    }
}