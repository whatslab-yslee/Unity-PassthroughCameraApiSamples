// Copyright (c) Meta Platforms, Inc. and affiliates.
// Modified for Hand Tracking with Blaze-Hand model.

using UnityEngine;
using Unity.Sentis;
using System.Collections.Generic;
using System.Collections;

namespace PassthroughCameraSamples.HandTracking
{
    public class HandTrackingManager : MonoBehaviour
    {
        [Header("Managers")]
        [SerializeField] private WebCamTextureManager m_webCamTextureManager;
        [SerializeField] private EnvironmentRayCastSampleManager m_environmentRaycast;

        [Header("Sentis Models")]
        [SerializeField] private ModelAsset m_handDetectorModelAsset;
        [SerializeField] private ModelAsset m_handLandmarkModelAsset;
        [SerializeField] private BackendType m_backend = BackendType.GPU;

        [Header("Hand Landmark Visualization")]
        [SerializeField] private GameObject m_landmarkPrefab; // 랜드마크 시각화를 위한 프리팹 (예: 작은 구)
        [SerializeField, Range(0, 1)] private float m_minDetectionConfidence = 0.7f;

        private IWorker m_detectorWorker;
        private IWorker m_landmarkWorker;

        private readonly List<GameObject> m_landmarkObjects = new List<GameObject>();
        private const int LandmarkCount = 21; // Blaze-Hand 모델의 랜드마크 수
        private bool m_isReady = false;

        private PassthroughCameraEye CameraEye => m_webCamTextureManager.Eye;

        private IEnumerator Start()
        {
            // 모델 로딩 전 잠시 대기
            yield return new WaitForSeconds(0.1f);

            // 랜드마크 시각화 오브젝트 풀 생성
            if (m_landmarkPrefab)
            {
                for (int i = 0; i < LandmarkCount; i++)
                {
                    GameObject landmark = Instantiate(m_landmarkPrefab, transform);
                    landmark.SetActive(false);
                    m_landmarkObjects.Add(landmark);
                }
            }

            // Sentis 모델 로드 및 Worker 생성
            var detectorModel = ModelLoader.Load(m_handDetectorModelAsset);
            m_detectorWorker = WorkerFactory.CreateWorker(m_backend, detectorModel);

            var landmarkModel = ModelLoader.Load(m_handLandmarkModelAsset);
            m_landmarkWorker = WorkerFactory.CreateWorker(m_backend, landmarkModel);

            m_isReady = true;
            Debug.Log("Hand Tracking Manager is ready.");
        }

        private void OnDestroy()
        {
            m_detectorWorker?.Dispose();
            m_landmarkWorker?.Dispose();
        }

        private void Update()
        {
            // 웹캠 텍스처나 모델이 준비되지 않았으면 실행하지 않음
            if (!m_isReady || m_webCamTextureManager.WebCamTexture == null || !m_webCamTextureManager.WebCamTexture.didUpdateThisFrame)
            {
                HideLandmarks();
                return;
            }

            // 1. 손 감지 (Hand Detector)
            // 입력 텐서 생성 (192x192)
            using var inputDetectorTensor = TextureConverter.ToTensor(m_webCamTextureManager.WebCamTexture, 192, 192, 3);
            m_detectorWorker.Execute(inputDetectorTensor);

            // 결과 텐서 가져오기
            var scoresTensor = m_detectorWorker.PeekOutput("scores") as TensorFloat;
            var boxesTensor = m_detectorWorker.PeekOutput("boxes") as TensorFloat;
            scoresTensor.MakeReadable();
            boxesTensor.MakeReadable();

            // 가장 신뢰도 높은 손 찾기
            float maxScore = -1;
            int bestHandIndex = -1;
            for (int i = 0; i < scoresTensor.shape[1]; i++)
            {
                if (scoresTensor[0, i, 0] > maxScore)
                {
                    maxScore = scoresTensor[0, i, 0];
                    bestHandIndex = i;
                }
            }
            
            // 신뢰도가 임계값보다 높으면 랜드마크 추적 실행
            if (maxScore > m_minDetectionConfidence)
            {
                // 2. 손 랜드마크 추적 (Hand Landmark)
                // 입력 텐서 생성 (224x224)
                using var inputLandmarkTensor = TextureConverter.ToTensor(m_webCamTextureManager.WebCamTexture, 224, 224, 3);
                m_landmarkWorker.Execute(inputLandmarkTensor);

                // 결과 텐서 가져오기
                var landmarkTensor = m_landmarkWorker.PeekOutput("landmarks") as TensorFloat;
                landmarkTensor.MakeReadable();
                
                // 3. 랜드마크 시각화
                VisualizeLandmarks(landmarkTensor);
            }
            else
            {
                // 손이 감지되지 않으면 랜드마크 숨기기
                HideLandmarks();
            }
        }

        private void VisualizeLandmarks(TensorFloat landmarkTensor)
        {
            var intrinsics = PassthroughCameraUtils.GetCameraIntrinsics(CameraEye);
            var camRes = intrinsics.Resolution;

            for (int i = 0; i < LandmarkCount; i++)
            {
                if (i >= m_landmarkObjects.Count) break;

                // 텐서에서 정규화된 x, y 좌표 추출 (모델 출력은 이미지 크기에 대해 정규화되어 있음)
                // Blaze-Hand Landmark 모델은 224x224 이미지에 대한 픽셀 좌표를 출력하므로 정규화 필요.
                float x = landmarkTensor[0, i, 0] / 224.0f;
                float y = landmarkTensor[0, i, 1] / 224.0f;

                // 2D 텍스처 좌표를 3D 월드 좌표로 변환
                var pixelCoords = new Vector2Int(Mathf.RoundToInt(x * camRes.x), Mathf.RoundToInt((1.0f - y) * camRes.y));
                var ray = PassthroughCameraUtils.ScreenPointToRayInWorld(CameraEye, pixelCoords);
                var worldPos = m_environmentRaycast.PlaceGameObjectByScreenPos(ray);

                GameObject landmarkObject = m_landmarkObjects[i];
                if (worldPos.HasValue)
                {
                    landmarkObject.transform.position = worldPos.Value;
                    landmarkObject.SetActive(true);
                }
                else
                {
                    // Raycast가 실패하면 카메라 앞 일정 거리에 표시
                    landmarkObject.transform.position = ray.GetPoint(0.5f);
                    landmarkObject.SetActive(true);
                }
            }
        }

        private void HideLandmarks()
        {
            foreach (var obj in m_landmarkObjects)
            {
                if (obj.activeSelf)
                {
                    obj.SetActive(false);
                }
            }
        }
    }
}