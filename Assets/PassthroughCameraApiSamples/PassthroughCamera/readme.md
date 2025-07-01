### PassthroughCamera/Editor
[PassthroughCameraEditorUpdateManifest.cs]
```csharp
// Copyright (c) Meta Platforms, Inc. and affiliates.

using System;
using System.Xml;
using Meta.XR.Samples;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace PassthroughCameraSamples.Editor
{
    [MetaCodeSample("PassthroughCameraApiSamples-PassthroughCamera")]
    public class PassthroughCameraEditorUpdateManifest : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;
        public void OnPreprocessBuild(BuildReport report)
        {
            UpdateAndroidManifest();
        }

        private void UpdateAndroidManifest()
        {
            var pcaManifestPermission = "horizonos.permission.HEADSET_CAMERA";
            var pcaManifestPassthroughFeature = "com.oculus.feature.PASSTHROUGH";
            var manifestFolder = Application.dataPath + "/Plugins/Android";
            try
            {
                // Load android manfiest file
                var doc = new XmlDocument();
                doc.Load(manifestFolder + "/AndroidManifest.xml");

                string androidNamepsaceURI;
                var element = (XmlElement)doc.SelectSingleNode("/manifest");
                if (element == null)
                {
                    throw new OperationCanceledException("Could not find manifest tag in android manifest.");
                }

                // Get android namespace URI from the manifest
                androidNamepsaceURI = element.GetAttribute("xmlns:android");
                if (!string.IsNullOrEmpty(androidNamepsaceURI))
                {
                    // Check if the android manifest has the Passthrough Feature enabled
                    var nodeList = doc.SelectNodes("/manifest/uses-feature");
                    var noPT = true;
                    foreach (XmlElement e in nodeList)
                    {
                        var attr = e.GetAttribute("name", androidNamepsaceURI);
                        if (attr == pcaManifestPassthroughFeature)
                        {
                            noPT = false;
                            break;
                        }
                    }
                    if (noPT)
                    {
                        throw new OperationCanceledException("To use the Passthrough Camera Access Api you need to enable Passthrough feature.");
                    }
                    else
                    {
                        // Check if the android manifest already has the Passthrough Camera Access permission
                        nodeList = doc.SelectNodes("/manifest/uses-permission");
                        foreach (XmlElement e in nodeList)
                        {
                            var attr = e.GetAttribute("name", androidNamepsaceURI);
                            if (attr == pcaManifestPermission)
                            {
                                Debug.Log("PCA Editor: Android manifest already has the proper permissions.");
                                return;
                            }
                        }

                        if (EditorUtility.DisplayDialog("Meta Passthrough Camera Access", "\"horizonos.permission.HEADSET_CAMERA\" permission IS NOT PRESENT in AndroidManifest.xml", "Add it", "Do Not Add it"))
                        {
                            element = (XmlElement)doc.SelectSingleNode("/manifest");
                            if (element != null)
                            {
                                // Insert Passthrough Camera Access permission
                                var newElement = doc.CreateElement("uses-permission");
                                _ = newElement.SetAttribute("name", androidNamepsaceURI, pcaManifestPermission);
                                _ = element.AppendChild(newElement);

                                doc.Save(manifestFolder + "/AndroidManifest.xml");
                                Debug.Log("PCA Editor: Successfully modified android manifest with Passthrough Camera Access permission.");
                                return;
                            }
                            throw new OperationCanceledException("Could not find android namespace URI in android manifest.");
                        }
                        else
                        {
                            throw new OperationCanceledException("To use the Passthrough Camera Access Api you need to add the \"horizonos.permission.HEADSET_CAMERA\" permission in your AndroidManifest.xml.");
                        }
                    }
                }
            }
            catch (Exception e)
            {
                throw new BuildFailedException("PCA Editor: " + e.Message);
            }
        }
    }
}

```

### PassthroughCamera/Scripts
[PassthroughCameraDebugger.cs]
```csharp
// Copyright (c) Meta Platforms, Inc. and affiliates.

using Meta.XR.Samples;
using UnityEngine;

namespace PassthroughCameraSamples
{
    [MetaCodeSample("PassthroughCameraApiSamples-PassthroughCamera")]
    public static class PassthroughCameraDebugger
    {
        public enum DebuglevelEnum
        {
            ALL,
            NONE,
            ONLY_ERROR,
            ONLY_LOG,
            ONLY_WARNING
        }

        public static DebuglevelEnum DebugLevel = DebuglevelEnum.ALL;

        /// <summary>
        /// Send debug information to Unity console based on DebugType and DebugLevel
        /// </summary>
        /// <param name="mType"></param>
        /// <param name="message"></param>
        public static void DebugMessage(LogType mType, string message)
        {
            switch (mType)
            {
                case LogType.Error:
                    if (DebugLevel is DebuglevelEnum.ALL or DebuglevelEnum.ONLY_ERROR)
                    {
                        Debug.LogError(message);
                    }
                    break;
                case LogType.Log:
                    if (DebugLevel is DebuglevelEnum.ALL or DebuglevelEnum.ONLY_LOG)
                    {
                        Debug.Log(message);
                    }
                    break;
                case LogType.Warning:
                    if (DebugLevel is DebuglevelEnum.ALL or DebuglevelEnum.ONLY_WARNING)
                    {
                        Debug.LogWarning(message);
                    }
                    break;
            }
        }
    }
}

```

[PassthroughCameraPermissions.cs]
```csharp
// Copyright (c) Meta Platforms, Inc. and affiliates.

using System.Collections.Generic;
using System.Linq;
using Meta.XR.Samples;
using UnityEngine;
#if UNITY_ANDROID
using UnityEngine.Android;
using PCD = PassthroughCameraSamples.PassthroughCameraDebugger;

#endif

namespace PassthroughCameraSamples
{
    /// <summary>
    /// PLEASE NOTE: Unity doesn't support requesting multiple permissions at the same time with <see cref="Permission.RequestUserPermissions"/> on Android.
    /// This component is a sample and shouldn't be used simultaneously with other scripts that manage Android permissions.
    /// </summary>
    [MetaCodeSample("PassthroughCameraApiSamples-PassthroughCamera")]
    public class PassthroughCameraPermissions : MonoBehaviour
    {
        [SerializeField] public List<string> PermissionRequestsOnStartup = new() { OVRPermissionsRequester.ScenePermission };

        public static readonly string[] CameraPermissions =
        {
            "android.permission.CAMERA",          // Required to use WebCamTexture object.
            "horizonos.permission.HEADSET_CAMERA" // Required to access the Passthrough Camera API in Horizon OS v74 and above.
        };

        public static bool? HasCameraPermission { get; private set; }
        private static bool s_askedOnce;

#if UNITY_ANDROID
        /// <summary>
        /// Request camera permission if the permission is not authorized by the user.
        /// </summary>
        public void AskCameraPermissions()
        {
            if (s_askedOnce)
            {
                return;
            }
            s_askedOnce = true;
            if (IsAllCameraPermissionsGranted())
            {
                HasCameraPermission = true;
                PCD.DebugMessage(LogType.Log, "PCA: All camera permissions granted.");
            }
            else
            {
                PCD.DebugMessage(LogType.Log, "PCA: Requesting camera permissions.");

                var callbacks = new PermissionCallbacks();
                callbacks.PermissionDenied += PermissionCallbacksPermissionDenied;
                callbacks.PermissionGranted += PermissionCallbacksPermissionGranted;
                callbacks.PermissionDeniedAndDontAskAgain += PermissionCallbacksPermissionDenied;

                // It's important to request all necessary permissions in one request because only one 'PermissionCallbacks' instance is supported at a time.
                var allPermissions = CameraPermissions.Concat(PermissionRequestsOnStartup).ToArray();
                Permission.RequestUserPermissions(allPermissions, callbacks);
            }
        }

        /// <summary>
        /// Permission Granted callback
        /// </summary>
        /// <param name="permissionName"></param>
        private static void PermissionCallbacksPermissionGranted(string permissionName)
        {
            PCD.DebugMessage(LogType.Log, $"PCA: Permission {permissionName} Granted");

            // Only initialize the WebCamTexture object if both permissions are granted
            if (IsAllCameraPermissionsGranted())
            {
                HasCameraPermission = true;
            }
        }

        /// <summary>
        /// Permission Denied callback.
        /// </summary>
        /// <param name="permissionName"></param>
        private static void PermissionCallbacksPermissionDenied(string permissionName)
        {
            PCD.DebugMessage(LogType.Warning, $"PCA: Permission {permissionName} Denied");
            HasCameraPermission = false;
            s_askedOnce = false;
        }

        private static bool IsAllCameraPermissionsGranted() => CameraPermissions.All(Permission.HasUserAuthorizedPermission);
#endif
    }
}

```

[PassthroughCameraUtils.cs]
```csharp
// Copyright (c) Meta Platforms, Inc. and affiliates.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Meta.XR.Samples;
using UnityEngine;
using UnityEngine.Assertions;

namespace PassthroughCameraSamples
{
    [MetaCodeSample("PassthroughCameraApiSamples-PassthroughCamera")]
    public static class PassthroughCameraUtils
    {
        // The Horizon OS starts supporting PCA with v74.
        public const int MINSUPPORTOSVERSION = 74;

        // The only pixel format supported atm
        private const int YUV_420_888 = 0x00000023;

        private static AndroidJavaObject s_currentActivity;
        private static AndroidJavaObject s_cameraManager;
        private static bool? s_isSupported;
        private static int? s_horizonOsVersion;

        // Caches
        internal static readonly Dictionary<PassthroughCameraEye, (string id, int index)> CameraEyeToCameraIdMap = new();
        private static readonly ConcurrentDictionary<PassthroughCameraEye, List<Vector2Int>> s_cameraOutputSizes = new();
        private static readonly ConcurrentDictionary<string, AndroidJavaObject> s_cameraCharacteristicsMap = new();
        private static readonly OVRPose?[] s_cachedCameraPosesRelativeToHead = new OVRPose?[2];

        /// <summary>
        /// Get the Horizon OS version number on the headset
        /// </summary>
        public static int? HorizonOSVersion
        {
            get
            {
                if (!s_horizonOsVersion.HasValue)
                {
                    var vrosClass = new AndroidJavaClass("vros.os.VrosBuild");
                    s_horizonOsVersion = vrosClass.CallStatic<int>("getSdkVersion");
#if OVR_INTERNAL_CODE
                    // 10000 means that the build doesn't have a proper release version, and it is still in Mainline,
                    // not in a release branch.
#endif // OVR_INTERNAL_CODE
                    if (s_horizonOsVersion == 10000)
                    {
                        s_horizonOsVersion = -1;
                    }
                }

                return s_horizonOsVersion.Value != -1 ? s_horizonOsVersion.Value : null;
            }
        }

        /// <summary>
        /// Returns true if the current headset supports Passthrough Camera API
        /// </summary>
        public static bool IsSupported
        {
            get
            {
                if (!s_isSupported.HasValue)
                {
                    var headset = OVRPlugin.GetSystemHeadsetType();
                    return (headset == OVRPlugin.SystemHeadset.Meta_Quest_3 ||
                            headset == OVRPlugin.SystemHeadset.Meta_Quest_3S) &&
                           (!HorizonOSVersion.HasValue || HorizonOSVersion >= MINSUPPORTOSVERSION);
                }

                return s_isSupported.Value;
            }
        }

        /// <summary>
        /// Provides a list of resolutions supported by the passthrough camera. Developers should use one of those
        /// when initializing the camera.
        /// </summary>
        /// <param name="cameraEye">The passthrough camera</param>
        public static List<Vector2Int> GetOutputSizes(PassthroughCameraEye cameraEye)
        {
            return s_cameraOutputSizes.GetOrAdd(cameraEye, GetOutputSizesInternal(cameraEye));
        }

        /// <summary>
        /// Returns the camera intrinsics for a specified passthrough camera. All the intrinsics values are provided
        /// in pixels. The resolution value is the maximum resolution available for the camera.
        /// </summary>
        /// <param name="cameraEye">The passthrough camera</param>
        public static PassthroughCameraIntrinsics GetCameraIntrinsics(PassthroughCameraEye cameraEye)
        {
            var cameraCharacteristics = GetCameraCharacteristics(cameraEye);
            var intrinsicsArr = GetCameraValueByKey<float[]>(cameraCharacteristics, "LENS_INTRINSIC_CALIBRATION");

            // Querying the camera resolution for which the intrinsics are provided
            // https://developer.android.com/reference/android/hardware/camera2/CameraCharacteristics#SENSOR_INFO_PRE_CORRECTION_ACTIVE_ARRAY_SIZE
            // This is a Rect of 4 elements: [bottom, left, right, top] with (0,0) at top-left corner.
            using var sensorSize = GetCameraValueByKey<AndroidJavaObject>(cameraCharacteristics, "SENSOR_INFO_PRE_CORRECTION_ACTIVE_ARRAY_SIZE");

            return new PassthroughCameraIntrinsics
            {
                FocalLength = new Vector2(intrinsicsArr[0], intrinsicsArr[1]),
                PrincipalPoint = new Vector2(intrinsicsArr[2], intrinsicsArr[3]),
                Resolution = new Vector2Int(sensorSize.Get<int>("right"), sensorSize.Get<int>("bottom")),
                Skew = intrinsicsArr[4]
            };
        }

        /// <summary>
        /// Returns an Android Camera2 API's cameraId associated with the passthrough camera specified in the argument.
        /// </summary>
        /// <param name="cameraEye">The passthrough camera</param>
        /// <exception cref="ApplicationException">Throws an exception if the code was not able to find cameraId</exception>
        public static string GetCameraIdByEye(PassthroughCameraEye cameraEye)
        {
            _ = EnsureInitialized();

            return !CameraEyeToCameraIdMap.TryGetValue(cameraEye, out var value)
                ? throw new ApplicationException($"Cannot find cameraId for the eye {cameraEye}")
                : value.id;
        }

        /// <summary>
        /// Returns the world pose of a passthrough camera at a given time.
        /// The LENS_POSE_TRANSLATION and LENS_POSE_ROTATION keys in 'android.hardware.camera2' are relative to the origin, so they can be cached to improve performance.
        /// </summary>
        /// <param name="cameraEye">The passthrough camera</param>
        /// <returns>The passthrough camera's world pose</returns>
        public static Pose GetCameraPoseInWorld(PassthroughCameraEye cameraEye)
        {
            var index = cameraEye == PassthroughCameraEye.Left ? 0 : 1;

            if (s_cachedCameraPosesRelativeToHead[index] == null)
            {
                var cameraId = GetCameraIdByEye(cameraEye);
                using var cameraCharacteristics = s_cameraManager.Call<AndroidJavaObject>("getCameraCharacteristics", cameraId);

                var cameraTranslation = GetCameraValueByKey<float[]>(cameraCharacteristics, "LENS_POSE_TRANSLATION");
                var p_headFromCamera = new Vector3(cameraTranslation[0], cameraTranslation[1], -cameraTranslation[2]);

                var cameraRotation = GetCameraValueByKey<float[]>(cameraCharacteristics, "LENS_POSE_ROTATION");
                var q_cameraFromHead = new Quaternion(-cameraRotation[0], -cameraRotation[1], cameraRotation[2], cameraRotation[3]);

                var q_headFromCamera = Quaternion.Inverse(q_cameraFromHead);

                s_cachedCameraPosesRelativeToHead[index] = new OVRPose
                {
                    position = p_headFromCamera,
                    orientation = q_headFromCamera
                };
            }

            var headFromCamera = s_cachedCameraPosesRelativeToHead[index].Value;
            var worldFromHead = OVRPlugin.GetNodePoseStateImmediate(OVRPlugin.Node.Head).Pose.ToOVRPose();
            var worldFromCamera = worldFromHead * headFromCamera;
            worldFromCamera.orientation *= Quaternion.Euler(180, 0, 0);

            return new Pose(worldFromCamera.position, worldFromCamera.orientation);
        }

        /// <summary>
        /// Returns a 3D ray in the world space which starts from the passthrough camera origin and passes through the
        /// 2D camera pixel.
        /// </summary>
        /// <param name="cameraEye">The passthrough camera</param>
        /// <param name="screenPoint">A 2D point on the camera texture. The point is positioned relative to the
        ///     maximum available camera resolution. This resolution can be obtained using <see cref="GetCameraIntrinsics"/>
        ///     or <see cref="GetOutputSizes"/> methods.
        /// </param>
        public static Ray ScreenPointToRayInWorld(PassthroughCameraEye cameraEye, Vector2Int screenPoint)
        {
            var rayInCamera = ScreenPointToRayInCamera(cameraEye, screenPoint);
            var cameraPoseInWorld = GetCameraPoseInWorld(cameraEye);
            var rayDirectionInWorld = cameraPoseInWorld.rotation * rayInCamera.direction;
            return new Ray(cameraPoseInWorld.position, rayDirectionInWorld);
        }

        /// <summary>
        /// Returns a 3D ray in the camera space which starts from the passthrough camera origin - which is always
        /// (0, 0, 0) - and passes through the 2D camera pixel.
        /// </summary>
        /// <param name="cameraEye">The passthrough camera</param>
        /// <param name="screenPoint">A 2D point on the camera texture. The point is positioned relative to the
        /// maximum available camera resolution. This resolution can be obtained using <see cref="GetCameraIntrinsics"/>
        /// or <see cref="GetOutputSizes"/> methods.
        /// </param>
        public static Ray ScreenPointToRayInCamera(PassthroughCameraEye cameraEye, Vector2Int screenPoint)
        {
            var intrinsics = GetCameraIntrinsics(cameraEye);
            var directionInCamera = new Vector3
            {
                x = (screenPoint.x - intrinsics.PrincipalPoint.x) / intrinsics.FocalLength.x,
                y = (screenPoint.y - intrinsics.PrincipalPoint.y) / intrinsics.FocalLength.y,
                z = 1
            };

            return new Ray(Vector3.zero, directionInCamera);
        }

        #region Private methods

        internal static bool EnsureInitialized()
        {
            if (CameraEyeToCameraIdMap.Count == 2)
            {
                return true;
            }

            Debug.Log($"PCA: PassthroughCamera - Initializing...");
            using var activityClass = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            s_currentActivity = activityClass.GetStatic<AndroidJavaObject>("currentActivity");
            s_cameraManager = s_currentActivity.Call<AndroidJavaObject>("getSystemService", "camera");
            Assert.IsNotNull(s_cameraManager, "Camera manager has not been provided by the Android system");

            var cameraIds = GetCameraIdList();
            Debug.Log($"PCA: PassthroughCamera - cameraId list is {string.Join(", ", cameraIds)}");

            for (var idIndex = 0; idIndex < cameraIds.Length; idIndex++)
            {
                var cameraId = cameraIds[idIndex];
                CameraSource? cameraSource = null;
                CameraPosition? cameraPosition = null;

                var cameraCharacteristics = GetCameraCharacteristics(cameraId);
                using var keysList = cameraCharacteristics.Call<AndroidJavaObject>("getKeys");
                var size = keysList.Call<int>("size");
                for (var i = 0; i < size; i++)
                {
                    using var key = keysList.Call<AndroidJavaObject>("get", i);
                    var keyName = key.Call<string>("getName");

                    if (string.Equals(keyName, "com.meta.extra_metadata.camera_source", StringComparison.OrdinalIgnoreCase))
                    {
                        // Both `com.meta.extra_metadata.camera_source` and `com.meta.extra_metadata.camera_source` are
                        // custom camera fields which are stored as arrays of size 1, instead of single values.
                        // We have to read those values correspondingly
                        var cameraSourceArr = GetCameraValueByKey<sbyte[]>(cameraCharacteristics, key);
                        if (cameraSourceArr == null || cameraSourceArr.Length != 1)
                            continue;

                        cameraSource = (CameraSource)cameraSourceArr[0];
                    }
                    else if (string.Equals(keyName, "com.meta.extra_metadata.position", StringComparison.OrdinalIgnoreCase))
                    {
                        var cameraPositionArr = GetCameraValueByKey<sbyte[]>(cameraCharacteristics, key);
                        if (cameraPositionArr == null || cameraPositionArr.Length != 1)
                            continue;

                        cameraPosition = (CameraPosition)cameraPositionArr[0];
                    }
                }

                if (!cameraSource.HasValue || !cameraPosition.HasValue || cameraSource.Value != CameraSource.Passthrough)
                    continue;

                switch (cameraPosition)
                {
                    case CameraPosition.Left:
                        Debug.Log($"PCA: Found left passthrough cameraId = {cameraId}");
                        CameraEyeToCameraIdMap[PassthroughCameraEye.Left] = (cameraId, idIndex);
                        break;
                    case CameraPosition.Right:
                        Debug.Log($"PCA: Found right passthrough cameraId = {cameraId}");
                        CameraEyeToCameraIdMap[PassthroughCameraEye.Right] = (cameraId, idIndex);
                        break;
                    default:
                        throw new ApplicationException($"Cannot parse Camera Position value {cameraPosition}");
                }
            }

            return CameraEyeToCameraIdMap.Count == 2;
        }

        internal static bool IsPassthroughEnabled()
        {
            return OVRManager.IsInsightPassthroughSupported() &&
                OVRManager.IsInsightPassthroughInitialized() &&
                OVRManager.instance.isInsightPassthroughEnabled;
        }

        private static string[] GetCameraIdList()
        {
            return s_cameraManager.Call<string[]>("getCameraIdList");
        }

        private static List<Vector2Int> GetOutputSizesInternal(PassthroughCameraEye cameraEye)
        {
            _ = EnsureInitialized();

            var cameraId = GetCameraIdByEye(cameraEye);
            var cameraCharacteristics = GetCameraCharacteristics(cameraId);
            using var configurationMap =
                GetCameraValueByKey<AndroidJavaObject>(cameraCharacteristics, "SCALER_STREAM_CONFIGURATION_MAP");
            var outputSizes = configurationMap.Call<AndroidJavaObject[]>("getOutputSizes", YUV_420_888);

            var result = new List<Vector2Int>();
            foreach (var outputSize in outputSizes)
            {
                var width = outputSize.Call<int>("getWidth");
                var height = outputSize.Call<int>("getHeight");
                result.Add(new Vector2Int(width, height));
            }

            foreach (var obj in outputSizes)
            {
                obj?.Dispose();
            }

            return result;
        }

        private static AndroidJavaObject GetCameraCharacteristics(string cameraId)
        {
            return s_cameraCharacteristicsMap.GetOrAdd(cameraId,
                _ => s_cameraManager.Call<AndroidJavaObject>("getCameraCharacteristics", cameraId));
        }

        private static AndroidJavaObject GetCameraCharacteristics(PassthroughCameraEye eye)
        {
            var cameraId = GetCameraIdByEye(eye);
            return GetCameraCharacteristics(cameraId);
        }

        private static T GetCameraValueByKey<T>(AndroidJavaObject cameraCharacteristics, string keyStr)
        {
            using var key = cameraCharacteristics.GetStatic<AndroidJavaObject>(keyStr);
            return GetCameraValueByKey<T>(cameraCharacteristics, key);
        }

        private static T GetCameraValueByKey<T>(AndroidJavaObject cameraCharacteristics, AndroidJavaObject key)
        {
            return cameraCharacteristics.Call<T>("get", key);
        }

        private enum CameraSource
        {
            Passthrough = 0
        }

        private enum CameraPosition
        {
            Left = 0,
            Right = 1
        }

        #endregion Private methods
    }

    /// <summary>
    /// Contains camera intrinsics, which describe physical characteristics of a passthrough camera
    /// </summary>
    public struct PassthroughCameraIntrinsics
    {
        /// <summary>
        /// The focal length in pixels
        /// </summary>
        public Vector2 FocalLength;
        /// <summary>
        /// The principal point from the top-left corner of the image, expressed in pixels
        /// </summary>
        public Vector2 PrincipalPoint;
        /// <summary>
        /// The resolution in pixels for which the intrinsics are defined
        /// </summary>
        public Vector2Int Resolution;
        /// <summary>
        /// The skew coefficient which represents the non-perpendicularity of the image sensor's x and y axes
        /// </summary>
        public float Skew;
    }
}

```

[WebCamTextureManager.cs]
```csharp
// Copyright (c) Meta Platforms, Inc. and affiliates.

using System.Collections;
using System.Linq;
using Meta.XR.Samples;
using UnityEngine;
using UnityEngine.Assertions;
using PCD = PassthroughCameraSamples.PassthroughCameraDebugger;

namespace PassthroughCameraSamples
{
    [MetaCodeSample("PassthroughCameraApiSamples-PassthroughCamera")]
    public class WebCamTextureManager : MonoBehaviour
    {
        [SerializeField] public PassthroughCameraEye Eye = PassthroughCameraEye.Left;
        [SerializeField, Tooltip("The requested resolution of the camera may not be supported by the chosen camera. In such cases, the closest available values will be used.\n\n" +
                                 "When set to (0,0), the highest supported resolution will be used.")]
        public Vector2Int RequestedResolution;
        [SerializeField] public PassthroughCameraPermissions CameraPermissions;

        /// <summary>
        /// Returns <see cref="WebCamTexture"/> reference if required permissions were granted and this component is enabled. Else, returns null.
        /// </summary>
        public WebCamTexture WebCamTexture { get; private set; }

        private bool m_hasPermission;

        private void Awake()
        {
            PCD.DebugMessage(LogType.Log, $"{nameof(WebCamTextureManager)}.{nameof(Awake)}() was called");
            Assert.AreEqual(1, FindObjectsByType<WebCamTextureManager>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length,
                $"PCA: Passthrough Camera: more than one {nameof(WebCamTextureManager)} component. Only one instance is allowed at a time. Current instance: {name}");
#if UNITY_ANDROID
            CameraPermissions.AskCameraPermissions();
#endif
        }

        private void OnEnable()
        {
            PCD.DebugMessage(LogType.Log, $"PCA: {nameof(OnEnable)}() was called");
            if (!PassthroughCameraUtils.IsSupported)
            {
                PCD.DebugMessage(LogType.Log, "PCA: Passthrough Camera functionality is not supported by the current device." +
                          $" Disabling {nameof(WebCamTextureManager)} object");
                enabled = false;
                return;
            }

            m_hasPermission = PassthroughCameraPermissions.HasCameraPermission == true;
            if (!m_hasPermission)
            {
                PCD.DebugMessage(LogType.Error,
                    $"PCA: Passthrough Camera requires permission(s) {string.Join(" and ", PassthroughCameraPermissions.CameraPermissions)}. Waiting for them to be granted...");
                return;
            }

            PCD.DebugMessage(LogType.Log, "PCA: All permissions have been granted");
            _ = StartCoroutine(InitializeWebCamTexture());
        }

        private void OnDisable()
        {
            PCD.DebugMessage(LogType.Log, $"PCA: {nameof(OnDisable)}() was called");
            StopCoroutine(InitializeWebCamTexture());
            if (WebCamTexture != null)
            {
                WebCamTexture.Stop();
                Destroy(WebCamTexture);
                WebCamTexture = null;
            }
        }

        private void Update()
        {
            if (!m_hasPermission)
            {
                if (PassthroughCameraPermissions.HasCameraPermission != true)
                    return;

                m_hasPermission = true;
                _ = StartCoroutine(InitializeWebCamTexture());
            }
        }

        private IEnumerator InitializeWebCamTexture()
        {
            // Check if Passhtrough is present in the scene and is enabled
            var ptLayer = FindAnyObjectByType<OVRPassthroughLayer>();
            if (ptLayer == null || !PassthroughCameraUtils.IsPassthroughEnabled())
            {
                PCD.DebugMessage(LogType.Error, "Passthrough must be enabled to use the Passthrough Camera API.");
                yield break;
            }

#if !UNITY_6000_OR_NEWER
            // There is a bug on Unity 2022 that causes a crash if you don't wait a frame before initializing the WebCamTexture.
            // Waiting for one frame is important and prevents the bug.
            yield return new WaitForEndOfFrame();
#endif

            while (true)
            {
                var devices = WebCamTexture.devices;
                if (PassthroughCameraUtils.EnsureInitialized() && PassthroughCameraUtils.CameraEyeToCameraIdMap.TryGetValue(Eye, out var cameraData))
                {
                    if (cameraData.index < devices.Length)
                    {
                        var deviceName = devices[cameraData.index].name;
                        WebCamTexture webCamTexture;
                        if (RequestedResolution == Vector2Int.zero)
                        {
                            var largestResolution = PassthroughCameraUtils.GetOutputSizes(Eye).OrderBy(static size => size.x * size.y).Last();
                            webCamTexture = new WebCamTexture(deviceName, largestResolution.x, largestResolution.y);
                        }
                        else
                        {
                            webCamTexture = new WebCamTexture(deviceName, RequestedResolution.x, RequestedResolution.y);
                        }
                        webCamTexture.Play();
                        var currentResolution = new Vector2Int(webCamTexture.width, webCamTexture.height);
                        if (RequestedResolution != Vector2Int.zero && RequestedResolution != currentResolution)
                        {
                            PCD.DebugMessage(LogType.Warning, $"WebCamTexture created, but '{nameof(RequestedResolution)}' {RequestedResolution} is not supported. Current resolution: {currentResolution}.");
                        }
                        WebCamTexture = webCamTexture;
                        PCD.DebugMessage(LogType.Log, $"WebCamTexture created, texturePtr: {WebCamTexture.GetNativeTexturePtr()}, size: {WebCamTexture.width}/{WebCamTexture.height}");
                        yield break;
                    }
                }

                PCD.DebugMessage(LogType.Error, $"Requested camera is not present in WebCamTexture.devices: {string.Join(", ", devices)}.");
                yield return null;
            }
        }
    }

    /// <summary>
    /// Defines the position of a passthrough camera relative to the headset
    /// </summary>
    public enum PassthroughCameraEye
    {
        Left,
        Right
    }
}

```