using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DeadfireMapTool
{
    public class ToggleTool : MonoBehaviour
    {
        MonoBehaviour behaviour;

        void Start()
        {
            behaviour = GetComponent<DeadfireMapToolBehaviour>();
        }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.F9))
                behaviour.enabled = !behaviour.enabled;
        }
    }

    public class DeadfireMapToolBehaviour : MonoBehaviour
    {
        // Reference to main and UI cameras
        Camera mainCamera, uiCamera;
        List<Transform> iconTransforms = new List<Transform>();

        bool init = false;

        const string MAIN_CAMERA = "Main Camera";
        const string WORLD_CAMERA = "Main Camera (World Map)";
        const string UI_CAMERA = "UICamera";

        const string LOG_MESSAGE_PATTERN = "{0}";
        const string LOG_WARNING_PATTERN = "<color=yellow>{0}</color>";
        const string LOG_ERROR_PATTERN = "<color=red>{0}</color>";

        const float FOV_DELTA = 5.0f;
        const float ROT_DELTA = 5.0f;
        const float FOV_SPEED = 7.5f;
        const float ROT_SPEED = 2.5f;

        const float LOG_UPDATE_INTERVAL = 0.5f;
        const float LOG_TIME = 5.0f;

        float fovTarget = 0.0f;
        float rotTarget = 0.0f;

        // Path to the user's desktop
        string desktopPath;

        float retryInitTimer = 0.0f;

        int scaleFactor = 1;
        int retries = 0;
        string capturePath;
        bool requestCapture;
        int requestCaptureFrame;
        float captureTimeoutTimer;
        float captureTimeout;
        bool showToolGuiLast = false;
        bool showWorldGuiLast = false;
        const float CAPTURE_CHECK_INTERVAL = 0.1f;
        const float CAPTURE_TIMEOUT = 30.0f;

        GUIStyle labelStyle;

        string logBufferString;
        
        void Start()
        {
            // Fetch path to the users desktop
            desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

            Debug.Log("DeadfireMapTool successfully loaded!");

            if (Init() == false)
                Debug.Log("Couldn't fetch all required references (is this the World Map scene?), retrying...");
        }

        void OnEnable()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
            Application.logMessageReceivedThreaded += OnLogMessage;
        }
        
        void OnDisable()
        {
            DeInit();
            SceneManager.sceneLoaded -= OnSceneLoaded;
            Application.logMessageReceivedThreaded -= OnLogMessage;
        }

        private void OnLogMessage(string message, string stackTrace, LogType type)
        {
            logBufferString += string.Format(type == LogType.Error ? LOG_ERROR_PATTERN :
                                             type == LogType.Warning ? LOG_WARNING_PATTERN : LOG_MESSAGE_PATTERN,
                                             message);

            if (string.IsNullOrEmpty(stackTrace))
                logBufferString += "\n<color=grey>" + stackTrace + "</color>";
        }

        private void OnSceneLoaded(Scene loadedScene, LoadSceneMode loadSceneMode)
        {
            Debug.Log("Loaded scene \"" + loadedScene.name + "\" (" + loadSceneMode + ")");
        }

        bool Init()
        {
            if (mainCamera == null)
            {
                // Get reference to Camera GameObjects
                var mainCameraObj = GameObject.Find(WORLD_CAMERA);

                // If this is an area map, the camera will be called something else
                //if (mainCameraObj == null)
                   // mainCameraObj = GameObject.Find(WORLD_CAMERA);

                if (mainCameraObj != null)
                {
                    Debug.Log("Found reference to Main Camera \"" + mainCameraObj.name + "\"");
                    mainCamera = mainCameraObj.GetComponent<Camera>();
                    fovTarget = mainCamera.fieldOfView;
                    rotTarget = mainCamera.transform.localEulerAngles.x;

                    // Disable scrolling to zoom (only for world map)
                    if (Game.CameraControl.Instance != null)
                        Game.CameraControl.Instance.EnablePlayerScroll(false);
                }
            }

            if (uiCamera == null)
            {
                var uiCameraObj = GameObject.Find(UI_CAMERA);

                if (uiCameraObj != null)
                {
                    Debug.Log("Found reference to UICamera.");
                    uiCamera = uiCameraObj.GetComponent<Camera>();
                }
            }

            var icons = new List<Game.WorldMapUsable>();
            //icons.AddRange(FindObjectsOfType<Game.WorldMapSceneIcon>());
            icons.AddRange(FindObjectsOfType<Game.WorldMapUsable>());

            if (icons != null && icons.Count > 0)
            {
                iconTransforms = new List<Transform>();

                foreach (var i in icons)
                {
                    i.ForceDiscover(); i.ForceIdentify();
                    iconTransforms.Add(i.transform);
                }
            }

            return init = (mainCamera != null && uiCamera != null);
        }
        
        void DeInit()
        {
            // Re-enable zoom scrolling
            if (Game.CameraControl.Instance != null)
                Game.CameraControl.Instance.EnablePlayerScroll(true);
        }
        

        void Update()
        {
            // Reattempt Init if we don't have either UICamera or Main Camera
            if (mainCamera == null || uiCamera == null)
            {
                // If we lost the references
                if (init)
                {
                    Debug.Log("Lost references, some functionality will be unavailable! Retrying...");
                    init = false;
                }

                // Increment the retryInitTimer
                retryInitTimer += Time.unscaledDeltaTime;

                // Retry every 1 second
                if (retryInitTimer > 1.0f)
                {
                    retryInitTimer = 0.0f;
                    Init();
                }
            }

            if (mainCamera != null)
                UpdateCamera();

            // Backslash to toggle UI
            if (Input.GetKeyDown(KeyCode.Backslash))
            {
                // If Ctrl-Backslash, toggle the tool UI
                if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))
                    showToolGui = !showToolGui;

                // If Backslash by itself, toggle the game UI
                else
                {
                    uiCamera.enabled = !uiCamera.enabled;
                    //uiCamera?.SetActive(!uiCamera.activeSelf);
                }
            }

            if (Input.GetKeyDown(KeyCode.PageUp))
                Game.Scripts.AdvanceTimeByHoursNoRest(1);
            if (Input.GetKeyDown(KeyCode.PageDown))
                Game.Scripts.AdvanceTimeByHoursNoRest(-1);

            if (Input.GetKeyDown(KeyCode.F11))
            {
                if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))
                {
                    scaleFactor++;
                    if (scaleFactor > 16)
                        scaleFactor = 1;
                }
                else
                {
                    showToolGuiLast = showToolGui;
                    showWorldGuiLast = uiCamera.enabled;

                    requestCapture = true;

                    // Sadly we can't call coroutines from injected code, they simply wont work, so waiting for the GUI to disappear has to be logic based
                    if (showToolGui == true || uiCamera?.enabled == true)
                    {
                        showToolGui = false;
                        uiCamera.enabled = false;

                        requestCaptureFrame = Time.frameCount;
                        Debug.Log("Capture requested at frame " + requestCaptureFrame);
                    }
                    else
                    {
                        // No UI is shown, free to capture (set requestCaptureFrame to max)
                        requestCaptureFrame = Int32.MaxValue;
                    }
                }
            }

            // Wait until a frame has passed (and the GUI has been hidden) before taking a screenshot
            if (requestCapture && Time.frameCount > requestCaptureFrame)
            {
                requestCapture = false;
                requestCaptureFrame = -1;
                retries = 1;

                capturePath = System.IO.Path.Combine(desktopPath, "poe2_wmt_" + DateTime.Now.ToFileTime() + ".png");
#if UNITY_2017_1_OR_NEWER
                ScreenCapture.CaptureScreenshot(capturePath, scaleFactor); 
#else
                Application.CaptureScreenshot(capturePath, scaleFactor);
#endif
                // Set captureTimeoutTimer and wait for the file to exist
                captureTimeoutTimer = 0.1f;
                captureTimeout = CAPTURE_CHECK_INTERVAL * retries;
            }
            
            if (captureTimeoutTimer > 0.0f)
            {
                captureTimeoutTimer -= Time.unscaledDeltaTime;

                // Re-show the UI once the capture exists, using linear backoff
                if (captureTimeoutTimer <= 0.0f)
                {
                    if (System.IO.File.Exists(capturePath))
                    {
                        Debug.Log("Captured screenshot to: " + capturePath);
                        showToolGui = showToolGuiLast;
                        uiCamera.enabled = showWorldGuiLast;
                        captureTimeoutTimer = -1.0f;
                    }
                    else
                    {
                        if (captureTimeout > CAPTURE_TIMEOUT)
                        {
                            Debug.Log("Capture timed out!");
                        }
                        else
                        {
                            retries++;
                            captureTimeoutTimer = CAPTURE_CHECK_INTERVAL * retries;
                            captureTimeout += captureTimeoutTimer;

                            Debug.Log("Waiting " + captureTimeoutTimer + "...");
                        }
                    }
                }
            }
        }

        void UpdateCamera()
        {
            // Use event.current instead of Input to enable consuming inputs
            Event e = Event.current;

            // F10 to revert rotation and fov to defaults
            if (Input.GetKeyDown(KeyCode.F10))
            {
                rotTarget = 65.0f;
                fovTarget = 40.0f;
                iconTransforms.ForEach(x => x.rotation = Quaternion.AngleAxis(rotTarget + 25f, Vector3.right));
            }

            // Scroll up and down to change both FOV and x axis rotation
            if (e.type == EventType.ScrollWheel)
            {
                // Hold shift to change x axis rotation
                if (e.modifiers == EventModifiers.Shift)
                {
                    rotTarget += ROT_DELTA * (e.delta.y > 0f ? 1.0f : -1.0f);
                    rotTarget = Mathf.Clamp(rotTarget, 5.0f, 90.0f);

                    // When rotating, point all the icons at the current camera rotation
                    iconTransforms.ForEach(x => x.rotation = Quaternion.AngleAxis(rotTarget + 25f, Vector3.right));
                }
                else
                {
                    fovTarget += FOV_DELTA * (e.delta.y > 0f ? 1.0f : -1.0f);
                    fovTarget = Mathf.Clamp(fovTarget, 5.0f, 175.0f);
                }

                // Consume this event
                e.Use();
            }

            // Lerp to the fovTarget
            if (!Mathf.Approximately(mainCamera.fieldOfView, fovTarget))
            {
                mainCamera.fieldOfView = Mathf.Lerp(mainCamera.fieldOfView, fovTarget, Time.unscaledDeltaTime * FOV_SPEED);
            }

            // Slerp to the rotational target
            if (!Mathf.Approximately(mainCamera.transform.eulerAngles.x, rotTarget))
            {
                mainCamera.transform.localRotation = Quaternion.Slerp(mainCamera.transform.localRotation, Quaternion.AngleAxis(rotTarget, Vector3.right), Time.unscaledDeltaTime * ROT_SPEED);
            }
        }

        Vector2 scrollPosition;
        Vector2 scrollViewPos = new Vector2((20f / 1920f) * Screen.width, (80f / 1080f) * Screen.height);
        Vector2 scrollViewSize = new Vector2((500f / 1920f) * Screen.width, (800f / 1080f) * Screen.height);
        Rect scrollViewRect;
        bool showToolGui = true;
        
        private void OnGUI()
        {
            if (labelStyle == null)
            {
                labelStyle = new GUIStyle(GUI.skin.label);
                labelStyle.richText = true;
                labelStyle.wordWrap = true;
                labelStyle.alignment = TextAnchor.LowerLeft;

                scrollViewRect = new Rect(scrollViewPos.x, scrollViewPos.y, scrollViewSize.x, scrollViewSize.y);
            }

            if (!showToolGui) return;

            var sb = new System.Text.StringBuilder();

            if (mainCamera != null)
            {
                sb.AppendLine(string.Format("fov: {0:0.00}, target: {1:0.00}", mainCamera.fieldOfView, fovTarget));
                sb.AppendLine(string.Format("rot: {0:0.00}, target: {1:0.00}", mainCamera.transform.localEulerAngles.x, rotTarget));
            }
            sb.AppendLine("Screenshot scale factor (Ctrl-F11): " + scaleFactor);
            GUI.Label(new Rect(20, 20, 400, 60), new GUIContent(sb.ToString()));

            GUI.Box(scrollViewRect, GUIContent.none);
            var content = new GUIContent(logBufferString);
            Rect viewRect = GUILayoutUtility.GetRect(content, labelStyle);
            viewRect.x = viewRect.y = 0.0f;
            scrollPosition = GUI.BeginScrollView(scrollViewRect, scrollPosition, viewRect);
            GUI.Label(viewRect, content, labelStyle);
            GUI.EndScrollView();
        }

        void PrintHierarchy(Transform obj, int depth)
        {
            Debug.Log(obj.name.PadLeft(obj.name.Length + (depth * 4), ' '));

            depth += 1;
            foreach (Transform child in obj)
                PrintHierarchy(child, depth);
        }
    }
}
