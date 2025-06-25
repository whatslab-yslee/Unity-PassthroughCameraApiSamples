// Copyright (c) Meta Platforms, Inc. and affiliates.
// Modified for Hand Tracking with Blaze-Hand model using Sentis 2.1 API.

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
        [SerializeField] private BackendType m_backend = BackendType.CPU;

        [Header("Hand Landmark Visualization")]
        [SerializeField] private GameObject m_landmarkPrefab; // 랜드마크 시각화를 위한 프리팹 (예: 작은 구)
        [SerializeField, Range(0, 1)] private float m_minDetectionConfidence = 0.7f;

        // IWorker는 모델로부터 직접 생성됩니다.
        private IWorker m_detectorWorker;
        private IWorker m_landmarkWorker;

        private readonly List<GameObject> m_landmarkObjects = new List<GameObject>();
        private const int LandmarkCount = 21;
        private bool m_isReady = false;

        private PassthroughCameraEye CameraEye => m_webCamTextureManager.Eye;

        private IEnumerator Start()
        {
            yield return new WaitForSeconds(0.1f);

            if (m_landmarkPrefab)
            {
                for (int i = 0; i < LandmarkCount; i++)
                {
                    GameObject landmark = Instantiate(m_landmarkPrefab, transform);
                    landmark.SetActive(false);
                    m_landmarkObjects.Add(landmark);
                }
            }

            // Sentis 모델 로드
            var detectorModel = ModelLoader.Load(m_handDetectorModelAsset);
            var landmarkModel = ModelLoader.Load(m_handLandmarkModelAsset);

            // [수정됨] WorkerFactory 대신 Model 객체에서 직접 Worker(엔진)를 생성합니다.
            m_detectorWorker = detectorModel.CreateWorker(m_backend);
            m_landmarkWorker = landmarkModel.CreateWorker(m_backend);

            m_isReady = true;
            Debug.Log("Hand Tracking Manager is ready using modern Sentis API.");
        }

        private void OnDestroy()
        {
            m_detectorWorker?.Dispose();
            m_landmarkWorker?.Dispose();
        }

        private void Update()
        {
            if (!m_isReady || m_webCamTextureManager.WebCamTexture == null || !m_webCamTextureManager.WebCamTexture.didUpdateThisFrame)
            {
                HideLandmarks();
                return;
            }

            // 1. 손 감지 (Hand Detector)
            using var inputDetectorTensor = TextureConverter.ToTensor(m_webCamTextureManager.WebCamTexture, 192, 192, 3);
            m_detectorWorker.Execute(inputDetectorTensor);
            
            var scoresTensor = m_detectorWorker.PeekOutput("scores") as Tensor<float>;
            var boxesTensor = m_detectorWorker.PeekOutput("boxes") as Tensor<float>;

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
            
            if (maxScore > m_minDetectionConfidence)
            {
                // 2. 손 랜드마크 추적 (Hand Landmark)
                using var inputLandmarkTensor = TextureConverter.ToTensor(m_webCamTextureManager.WebCamTexture, 224, 224, 3);
                m_landmarkWorker.Execute(inputLandmarkTensor);

                var landmarkTensor = m_landmarkWorker.PeekOutput("landmarks") as Tensor<float>;
                
                // 3. 랜드마크 시각화
                VisualizeLandmarks(landmarkTensor);
            }
            else
            {
                HideLandmarks();
            }
        }

        private void VisualizeLandmarks(Tensor<float> landmarkTensor)
        {
            var intrinsics = PassthroughCameraUtils.GetCameraIntrinsics(CameraEye);
            var camRes = intrinsics.Resolution;

            for (int i = 0; i < LandmarkCount; i++)
            {
                if (i >= m_landmarkObjects.Count) break;

                float x = landmarkTensor[0, i, 0];
                float y = landmarkTensor[0, i, 1];

                float normalizedX = x / 224.0f;
                float normalizedY = y / 224.0f;

                var pixelCoords = new Vector2Int(Mathf.RoundToInt(normalizedX * camRes.x), Mathf.RoundToInt((1.0f - normalizedY) * camRes.y));
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