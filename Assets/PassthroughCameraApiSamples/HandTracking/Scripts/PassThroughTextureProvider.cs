using System.Collections;
using UnityEngine;
using UnityEngine.Assertions;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

// Passthrough 카메라 영상을 관리하고 제공하는 스크립트
namespace PassthroughCameraSamples.HandTracking
{
    [MetaCodeSample("PassthroughCameraApiSamples-HandTracking")]
    public class PassthroughTextureProvider : MonoBehaviour
    {
        [SerializeField] private PassthroughCameraEye m_eye = PassthroughCameraEye.Right;
        public PassthroughCameraEye Eye => m_eye;

        private WebCamTexture m_webCamTexture;
        public Texture WebCamTexture => m_webCamTexture;

        private bool m_permissionGranted = false;

        private IEnumerator Start()
        {
    #if UNITY_ANDROID && !UNITY_EDITOR
            if (!Permission.HasUserAuthorizedPermission("android.permission.CAMERA"))
            {
                var callbacks = new PermissionCallbacks();
                callbacks.PermissionGranted += (permission) => { m_permissionGranted = true; };
                callbacks.PermissionDenied += (permission) => { Debug.LogError("카메라 권한이 거부되었습니다."); };
                callbacks.PermissionDeniedAndDontAskAgain += (permission) => { Debug.LogError("카메라 권한이 거부되었으며 다시 묻지 않도록 설정되었습니다."); };
                Permission.RequestUserPermission("android.permission.CAMERA", callbacks);
            }
            else
            {
                m_permissionGranted = true;
            }
    #else
            m_permissionGranted = true;
    #endif
            yield return new WaitUntil(() => m_permissionGranted);

            InitializeWebCamTexture();
        }

        private void InitializeWebCamTexture()
        {
            var devices = WebCamTexture.devices;
            if (devices.Length == 0)
            {
                Debug.LogError("사용 가능한 카메라 장치가 없습니다.");
                return;
            }

            // Oculus 기기는 보통 첫 번째 카메라를 사용합니다.
            vardeviceName = devices[0].name;
            var intrinsics = PassthroughCameraUtils.GetCameraIntrinsics(m_eye);
            m_webCamTexture = new WebCamTexture(deviceName, intrinsics.Resolution.x, intrinsics.Resolution.y);
            m_webCamTexture.Play();

            Debug.Log($"WebCamTexture 초기화 완료: {m_webCamTexture.deviceName}");
        }

        private void OnDestroy()
        {
            if (m_webCamTexture != null)
            {
                m_webCamTexture.Stop();
            }
        }
    }
}