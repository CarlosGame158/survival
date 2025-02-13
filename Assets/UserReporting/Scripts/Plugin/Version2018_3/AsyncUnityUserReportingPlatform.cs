﻿using System;
using System.Collections.Generic;
using System.Globalization;
using Assets.UserReporting.Scripts.Plugin;
using Unity.Cloud.UserReporting.Client;
using Unity.Screenshots;
using UnityEngine;
using UnityEngine.Analytics;
using UnityEngine.Networking;
using UnityEngine.Profiling;
using UnityEngine.SceneManagement;

namespace Unity.Cloud.UserReporting.Plugin.Version2018_3
{
    /// <summary>
    ///     Represents a Unity user reporting platform that supports async operations for screetshotting and user report
    ///     creation.
    /// </summary>
    public class AsyncUnityUserReportingPlatform : IUserReportingPlatform, ILogListener
    {
        #region Constructors

        /// <summary>
        ///     Creates a new instance of the <see cref="UnityUserReportingPlatform" /> class.
        /// </summary>
        public AsyncUnityUserReportingPlatform()
        {
            logMessages = new List<LogMessage>();
            postOperations = new List<PostOperation>();
            screenshotManager = new ScreenshotManager();

            // Recorders
            profilerSamplers = new List<ProfilerSampler>();
            var samplerNames = GetSamplerNames();
            foreach (var kvp in samplerNames)
            {
                var sampler = Sampler.Get(kvp.Key);
                if (sampler.isValid)
                {
                    var recorder = sampler.GetRecorder();
                    recorder.enabled = true;
                    var profilerSampler = new ProfilerSampler();
                    profilerSampler.Name = kvp.Value;
                    profilerSampler.Recorder = recorder;
                    profilerSamplers.Add(profilerSampler);
                }
            }

            // Log Messages
            LogDispatcher.Register(this);
        }

        #endregion

        #region Nested Types

        /// <summary>
        ///     Represents a log message.
        /// </summary>
        private struct LogMessage
        {
            #region Fields

            /// <summary>
            ///     Gets or sets the log string.
            /// </summary>
            public string LogString;

            /// <summary>
            ///     Gets or sets the log type.
            /// </summary>
            public LogType LogType;

            /// <summary>
            ///     Gets or sets the stack trace.
            /// </summary>
            public string StackTrace;

            #endregion
        }

        /// <summary>
        ///     Represents a post operation.
        /// </summary>
        private class PostOperation
        {
            #region Properties

            /// <summary>
            ///     Gets or sets the callback.
            /// </summary>
            public Action<bool, byte[]> Callback { get; set; }

            /// <summary>
            ///     Gets or sets the progress callback.
            /// </summary>
            public Action<float, float> ProgressCallback { get; set; }

            /// <summary>
            ///     Gets or sets the web request.
            /// </summary>
            public UnityWebRequest WebRequest { get; set; }

            #endregion
        }

        /// <summary>
        ///     Represents a profiler sampler.
        /// </summary>
        private struct ProfilerSampler
        {
            #region Fields

            /// <summary>
            ///     Gets or sets the name.
            /// </summary>
            public string Name;

            /// <summary>
            ///     Gets or sets the recorder.
            /// </summary>
            public Recorder Recorder;

            #endregion

            #region Methods

            /// <summary>
            ///     Gets the value of the sampler.
            /// </summary>
            /// <returns>The value of the sampler.</returns>
            public double GetValue()
            {
                if (Recorder == null) return 0;
                return Recorder.elapsedNanoseconds / 1000000.0;
            }

            #endregion
        }

        #endregion

        #region Fields

        private readonly List<LogMessage> logMessages;

        private readonly List<PostOperation> postOperations;

        private readonly List<ProfilerSampler> profilerSamplers;

        private readonly ScreenshotManager screenshotManager;

        private List<PostOperation> taskOperations;

        #endregion

        #region Methods

        /// <inheritdoc cref="IUserReportingPlatform" />
        public T DeserializeJson<T>(string json)
        {
            return SimpleJson.SimpleJson.DeserializeObject<T>(json);
        }

        /// <inheritdoc cref="IUserReportingPlatform" />
        public void OnEndOfFrame(UserReportingClient client)
        {
            screenshotManager.OnEndOfFrame();
        }

        /// <inheritdoc cref="IUserReportingPlatform" />
        public void Post(string endpoint, string contentType, byte[] content, Action<float, float> progressCallback,
            Action<bool, byte[]> callback)
        {
            var webRequest = new UnityWebRequest(endpoint, "POST");
            webRequest.uploadHandler = new UploadHandlerRaw(content);
            webRequest.downloadHandler = new DownloadHandlerBuffer();
            webRequest.SetRequestHeader("Content-Type", contentType);
            webRequest.SendWebRequest();
            var postOperation = new PostOperation();
            postOperation.WebRequest = webRequest;
            postOperation.Callback = callback;
            postOperation.ProgressCallback = progressCallback;
            postOperations.Add(postOperation);
        }

        public void ReceiveLogMessage(string logString, string stackTrace, LogType logType)
        {
            lock (logMessages)
            {
                var logMessage = new LogMessage();
                logMessage.LogString = logString;
                logMessage.StackTrace = stackTrace;
                logMessage.LogType = logType;
                logMessages.Add(logMessage);
            }
        }

        /// <inheritdoc cref="IUserReportingPlatform" />
        public void RunTask(Func<object> task, Action<object> callback)
        {
            callback(task());
        }

        /// <inheritdoc cref="IUserReportingPlatform" />
        public void SendAnalyticsEvent(string eventName, Dictionary<string, object> eventData)
        {
            Analytics.CustomEvent(eventName, eventData);
        }

        /// <inheritdoc cref="IUserReportingPlatform" />
        public string SerializeJson(object instance)
        {
            return SimpleJson.SimpleJson.SerializeObject(instance);
        }

        /// <inheritdoc cref="IUserReportingPlatform" />
        public void TakeScreenshot(int frameNumber, int maximumWidth, int maximumHeight, object source,
            Action<int, byte[]> callback)
        {
            screenshotManager.TakeScreenshot(source, frameNumber, maximumWidth, maximumHeight, callback);
        }

        /// <inheritdoc cref="IUserReportingPlatform" />
        public void Update(UserReportingClient client)
        {
            // Log Messages
            lock (logMessages)
            {
                foreach (var logMessage in logMessages)
                {
                    var eventLevel = UserReportEventLevel.Info;
                    if (logMessage.LogType == LogType.Warning)
                        eventLevel = UserReportEventLevel.Warning;
                    else if (logMessage.LogType == LogType.Error)
                        eventLevel = UserReportEventLevel.Error;
                    else if (logMessage.LogType == LogType.Exception)
                        eventLevel = UserReportEventLevel.Error;
                    else if (logMessage.LogType == LogType.Assert) eventLevel = UserReportEventLevel.Error;
                    if (client.IsConnectedToLogger)
                        client.LogEvent(eventLevel, logMessage.LogString, logMessage.StackTrace);
                }

                logMessages.Clear();
            }

            // Metrics
            if (client.Configuration.MetricsGatheringMode == MetricsGatheringMode.Automatic)
            {
                // Sample Automatic Metrics
                SampleAutomaticMetrics(client);

                // Profiler Samplers
                foreach (var profilerSampler in profilerSamplers)
                    client.SampleMetric(profilerSampler.Name, profilerSampler.GetValue());
            }

            // Post Operations
            var postOperationIndex = 0;
            while (postOperationIndex < postOperations.Count)
            {
                var postOperation = postOperations[postOperationIndex];
                if (postOperation.WebRequest.isDone)
                {
                    var isError = postOperation.WebRequest.error != null &&
                                  postOperation.WebRequest.responseCode != 200;
                    if (isError)
                    {
                        var errorMessage = string.Format("UnityUserReportingPlatform.Post: {0} {1}",
                            postOperation.WebRequest.responseCode, postOperation.WebRequest.error);
                        Debug.Log(errorMessage);
                        client.LogEvent(UserReportEventLevel.Error, errorMessage);
                    }

                    postOperation.ProgressCallback(1, 1);
                    postOperation.Callback(!isError, postOperation.WebRequest.downloadHandler.data);
                    postOperations.Remove(postOperation);
                }
                else
                {
                    postOperation.ProgressCallback(postOperation.WebRequest.uploadProgress,
                        postOperation.WebRequest.downloadProgress);
                    postOperationIndex++;
                }
            }
        }

        #endregion

        #region Virtual Methods

        /// <inheritdoc cref="IUserReportingPlatform" />
        public virtual IDictionary<string, string> GetDeviceMetadata()
        {
            var deviceMetadata = new Dictionary<string, string>();

            // Unity
            deviceMetadata.Add("BuildGUID", Application.buildGUID);
            deviceMetadata.Add("DeviceModel", SystemInfo.deviceModel);
            deviceMetadata.Add("DeviceType", SystemInfo.deviceType.ToString());
            deviceMetadata.Add("DPI", Screen.dpi.ToString(CultureInfo.InvariantCulture));
            deviceMetadata.Add("GraphicsDeviceName", SystemInfo.graphicsDeviceName);
            deviceMetadata.Add("GraphicsDeviceType", SystemInfo.graphicsDeviceType.ToString());
            deviceMetadata.Add("GraphicsDeviceVendor", SystemInfo.graphicsDeviceVendor);
            deviceMetadata.Add("GraphicsDeviceVersion", SystemInfo.graphicsDeviceVersion);
            deviceMetadata.Add("GraphicsMemorySize", SystemInfo.graphicsMemorySize.ToString());
            deviceMetadata.Add("InstallerName", Application.installerName);
            deviceMetadata.Add("InstallMode", Application.installMode.ToString());
            deviceMetadata.Add("IsEditor", Application.isEditor.ToString());
            deviceMetadata.Add("IsFullScreen", Screen.fullScreen.ToString());
            deviceMetadata.Add("OperatingSystem", SystemInfo.operatingSystem);
            deviceMetadata.Add("OperatingSystemFamily", SystemInfo.operatingSystemFamily.ToString());
            deviceMetadata.Add("Orientation", Screen.orientation.ToString());
            deviceMetadata.Add("Platform", Application.platform.ToString());
            try
            {
                deviceMetadata.Add("QualityLevel", QualitySettings.names[QualitySettings.GetQualityLevel()]);
            }
            catch
            {
                // Empty
            }

            deviceMetadata.Add("ResolutionWidth", Screen.currentResolution.width.ToString());
            deviceMetadata.Add("ResolutionHeight", Screen.currentResolution.height.ToString());
            deviceMetadata.Add("ResolutionRefreshRate", Screen.currentResolution.refreshRate.ToString());
            deviceMetadata.Add("SystemLanguage", Application.systemLanguage.ToString());
            deviceMetadata.Add("SystemMemorySize", SystemInfo.systemMemorySize.ToString());
            deviceMetadata.Add("TargetFrameRate", Application.targetFrameRate.ToString());
            deviceMetadata.Add("UnityVersion", Application.unityVersion);
            deviceMetadata.Add("Version", Application.version);

            // Other
            deviceMetadata.Add("Source", "Unity");

            // Type
            var type = GetType();
            deviceMetadata.Add("IUserReportingPlatform", type.Name);

            // Return
            return deviceMetadata;
        }

        public virtual Dictionary<string, string> GetSamplerNames()
        {
            var samplerNames = new Dictionary<string, string>();
            samplerNames.Add("AudioManager.FixedUpdate", "AudioManager.FixedUpdateInMilliseconds");
            samplerNames.Add("AudioManager.Update", "AudioManager.UpdateInMilliseconds");
            samplerNames.Add("LateBehaviourUpdate", "Behaviors.LateUpdateInMilliseconds");
            samplerNames.Add("BehaviourUpdate", "Behaviors.UpdateInMilliseconds");
            samplerNames.Add("Camera.Render", "Camera.RenderInMilliseconds");
            samplerNames.Add("Overhead", "Engine.OverheadInMilliseconds");
            samplerNames.Add("WaitForRenderJobs", "Engine.WaitForRenderJobsInMilliseconds");
            samplerNames.Add("WaitForTargetFPS", "Engine.WaitForTargetFPSInMilliseconds");
            samplerNames.Add("GUI.Repaint", "GUI.RepaintInMilliseconds");
            samplerNames.Add("Network.Update", "Network.UpdateInMilliseconds");
            samplerNames.Add("ParticleSystem.EndUpdateAll", "ParticleSystem.EndUpdateAllInMilliseconds");
            samplerNames.Add("ParticleSystem.Update", "ParticleSystem.UpdateInMilliseconds");
            samplerNames.Add("Physics.FetchResults", "Physics.FetchResultsInMilliseconds");
            samplerNames.Add("Physics.Processing", "Physics.ProcessingInMilliseconds");
            samplerNames.Add("Physics.ProcessReports", "Physics.ProcessReportsInMilliseconds");
            samplerNames.Add("Physics.Simulate", "Physics.SimulateInMilliseconds");
            samplerNames.Add("Physics.UpdateBodies", "Physics.UpdateBodiesInMilliseconds");
            samplerNames.Add("Physics.Interpolation", "Physics.InterpolationInMilliseconds");
            samplerNames.Add("Physics2D.DynamicUpdate", "Physics2D.DynamicUpdateInMilliseconds");
            samplerNames.Add("Physics2D.FixedUpdate", "Physics2D.FixedUpdateInMilliseconds");
            return samplerNames;
        }

        /// <inheritdoc cref="IUserReportingPlatform" />
        public virtual void ModifyUserReport(UserReport userReport)
        {
            // Active Scene
            var activeScene = SceneManager.GetActiveScene();
            userReport.DeviceMetadata.Add(new UserReportNamedValue("ActiveSceneName", activeScene.name));

            // Main Camera
            var mainCamera = Camera.main;
            if (mainCamera != null)
            {
                userReport.DeviceMetadata.Add(new UserReportNamedValue("MainCameraName", mainCamera.name));
                userReport.DeviceMetadata.Add(new UserReportNamedValue("MainCameraPosition",
                    mainCamera.transform.position.ToString()));
                userReport.DeviceMetadata.Add(new UserReportNamedValue("MainCameraForward",
                    mainCamera.transform.forward.ToString()));

                // Looking At
                RaycastHit hit;
                if (Physics.Raycast(mainCamera.transform.position, mainCamera.transform.forward, out hit))
                {
                    var lookingAt = hit.transform.gameObject;
                    userReport.DeviceMetadata.Add(new UserReportNamedValue("LookingAt", hit.point.ToString()));
                    userReport.DeviceMetadata.Add(new UserReportNamedValue("LookingAtGameObject", lookingAt.name));
                    userReport.DeviceMetadata.Add(new UserReportNamedValue("LookingAtGameObjectPosition",
                        lookingAt.transform.position.ToString()));
                }
            }
        }

        /// <summary>
        ///     Samples automatic metrics.
        /// </summary>
        /// <param name="client">The client.</param>
        public virtual void SampleAutomaticMetrics(UserReportingClient client)
        {
            // Graphics
            client.SampleMetric("Graphics.FramesPerSecond", 1.0f / Time.deltaTime);

            // Memory
            client.SampleMetric("Memory.MonoUsedSizeInBytes", Profiler.GetMonoUsedSizeLong());
            client.SampleMetric("Memory.TotalAllocatedMemoryInBytes", Profiler.GetTotalAllocatedMemoryLong());
            client.SampleMetric("Memory.TotalReservedMemoryInBytes", Profiler.GetTotalReservedMemoryLong());
            client.SampleMetric("Memory.TotalUnusedReservedMemoryInBytes", Profiler.GetTotalUnusedReservedMemoryLong());

            // Battery
            client.SampleMetric("Battery.BatteryLevelInPercent", SystemInfo.batteryLevel);
        }

        #endregion
    }
}