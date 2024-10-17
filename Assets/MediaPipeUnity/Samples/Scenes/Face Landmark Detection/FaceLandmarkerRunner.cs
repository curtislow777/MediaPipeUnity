using System.Collections;
using System.Collections.Generic;
using Mediapipe.Tasks.Vision.FaceLandmarker;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using TMPro;

namespace Mediapipe.Unity.Sample.FaceLandmarkDetection
{
  public class FaceLandmarkerRunner : VisionTaskApiRunner<FaceLandmarker>
  {
    [SerializeField] private FaceLandmarkerResultAnnotationController _faceLandmarkerResultAnnotationController;

    private Experimental.TextureFramePool _textureFramePool;

    public readonly FaceLandmarkDetectionConfig config = new FaceLandmarkDetectionConfig();

    // Landmarks for the left and right eye for EAR calculation
    private readonly int[] leftEyeIndices = new int[] { 362, 387, 385, 263, 380, 373 }; 
    private readonly int[] rightEyeIndices = new int[] { 33, 160, 158, 133, 153, 144 };



    private const float EAR_THRESHOLD = 0.25f;  // Adjust threshold if needed

    // Blink detection variables
    private float smoothedEAR = 0f;
    private bool isBlinking = false;
    private float blinkCooldown = 0.15f;  // Cooldown time between blinks (in seconds)
    private float blinkTimer = 0f;
    private int bufferCount = 5;  // Number of frames to average EAR values for smoothing
    private Queue<float> earBuffer = new Queue<float>();  // Buffer for smoothing EAR values

    public DebugUI debugUI;  // Reference to the UI script for text updates

    public override void Stop()
    {
      base.Stop();
      _textureFramePool?.Dispose();
      _textureFramePool = null;
    }

    protected override IEnumerator Run()
    {
      Debug.Log($"Delegate = {config.Delegate}");
      Debug.Log($"Running Mode = {config.RunningMode}");
      Debug.Log($"NumFaces = {config.NumFaces}");
      Debug.Log($"MinFaceDetectionConfidence = {config.MinFaceDetectionConfidence}");
      Debug.Log($"MinFacePresenceConfidence = {config.MinFacePresenceConfidence}");
      Debug.Log($"MinTrackingConfidence = {config.MinTrackingConfidence}");
      Debug.Log($"OutputFaceBlendshapes = {config.OutputFaceBlendshapes}");
      Debug.Log($"OutputFacialTransformationMatrixes = {config.OutputFacialTransformationMatrixes}");

      yield return AssetLoader.PrepareAssetAsync(config.ModelPath);

      var options = config.GetFaceLandmarkerOptions(config.RunningMode == Tasks.Vision.Core.RunningMode.LIVE_STREAM ? OnFaceLandmarkDetectionOutput : null);
      taskApi = FaceLandmarker.CreateFromOptions(options, GpuManager.GpuResources);
      var imageSource = ImageSourceProvider.ImageSource;

      yield return imageSource.Play();

      if (!imageSource.isPrepared)
      {
        Debug.LogError("Failed to start ImageSource, exiting...");
        yield break;
      }

      _textureFramePool = new Experimental.TextureFramePool(imageSource.textureWidth, imageSource.textureHeight, TextureFormat.RGBA32, 10);

      screen.Initialize(imageSource);

      SetupAnnotationController(_faceLandmarkerResultAnnotationController, imageSource);

      var transformationOptions = imageSource.GetTransformationOptions();
      var flipHorizontally = transformationOptions.flipHorizontally;
      var flipVertically = transformationOptions.flipVertically;
      var imageProcessingOptions = new Tasks.Vision.Core.ImageProcessingOptions(rotationDegrees: (int)transformationOptions.rotationAngle);

      AsyncGPUReadbackRequest req = default;
      var waitUntilReqDone = new WaitUntil(() => req.done);
      var result = FaceLandmarkerResult.Alloc(options.numFaces);

      var canUseGpuImage = options.baseOptions.delegateCase == Tasks.Core.BaseOptions.Delegate.GPU &&
        SystemInfo.graphicsDeviceType == GraphicsDeviceType.OpenGLES3 &&
        GpuManager.GpuResources != null;
      using var glContext = canUseGpuImage ? GpuManager.GetGlContext() : null;

      while (true)
      {
        if (isPaused)
        {
          yield return new WaitWhile(() => isPaused);
        }

        if (!_textureFramePool.TryGetTextureFrame(out var textureFrame))
        {
          yield return new WaitForEndOfFrame();
          continue;
        }

        Image image;
        if (canUseGpuImage)
        {
          yield return new WaitForEndOfFrame();
          textureFrame.ReadTextureOnGPU(imageSource.GetCurrentTexture(), flipHorizontally, flipVertically);
          image = textureFrame.BuildGpuImage(glContext);
        }
        else
        {
          req = textureFrame.ReadTextureAsync(imageSource.GetCurrentTexture(), flipHorizontally, flipVertically);
          yield return waitUntilReqDone;

          if (req.hasError)
          {
            Debug.LogError($"Failed to read texture from the image source, exiting...");
            break;
          }
          image = textureFrame.BuildCPUImage();
          textureFrame.Release();
        }

        switch (taskApi.runningMode)
        {
          case Tasks.Vision.Core.RunningMode.IMAGE:
            if (taskApi.TryDetect(image, imageProcessingOptions, ref result))
            {
              _faceLandmarkerResultAnnotationController.DrawNow(result);
            }
            else
            {
              _faceLandmarkerResultAnnotationController.DrawNow(default);
            }
            break;
          case Tasks.Vision.Core.RunningMode.VIDEO:
            if (taskApi.TryDetectForVideo(image, GetCurrentTimestampMillisec(), imageProcessingOptions, ref result))
            {
              _faceLandmarkerResultAnnotationController.DrawNow(result);
            }
            else
            {
              _faceLandmarkerResultAnnotationController.DrawNow(default);
            }
            break;
          case Tasks.Vision.Core.RunningMode.LIVE_STREAM:
            taskApi.DetectAsync(image, GetCurrentTimestampMillisec(), imageProcessingOptions);
            break;
        }
      }
    }

    private void OnFaceLandmarkDetectionOutput(FaceLandmarkerResult result, Image image, long timestamp)
    {
      _faceLandmarkerResultAnnotationController.DrawLater(result);

      if (result.faceLandmarks != null && result.faceLandmarks.Count > 0)
      {
        var landmarks = result.faceLandmarks[0].landmarks;

        if (landmarks != null && landmarks.Count > 0)
        {
          // Extract landmarks for both eyes
          Vector3[] leftEyeLandmarks = new Vector3[6];
          Vector3[] rightEyeLandmarks = new Vector3[6];

          for (int i = 0; i < leftEyeIndices.Length; i++)
          {
            leftEyeLandmarks[i] = new Vector3(landmarks[leftEyeIndices[i]].x, landmarks[leftEyeIndices[i]].y, landmarks[leftEyeIndices[i]].z);
            rightEyeLandmarks[i] = new Vector3(landmarks[rightEyeIndices[i]].x, landmarks[rightEyeIndices[i]].y, landmarks[rightEyeIndices[i]].z);
          }

          // Calculate EAR for both eyes
          float leftEAR = CalculateEAR(leftEyeLandmarks);
          float rightEAR = CalculateEAR(rightEyeLandmarks);

          // Average EAR for blink detection
          float avgEAR = (leftEAR + rightEAR) / 2.0f;

          // Add EAR to buffer for smoothing
          if (earBuffer.Count >= bufferCount)
          {
            earBuffer.Dequeue();  // Remove oldest EAR value
          }
          earBuffer.Enqueue(avgEAR);  // Add current EAR value

          // Calculate smoothed EAR
          smoothedEAR = 0f;
          foreach (float ear in earBuffer)
          {
            smoothedEAR += ear;
          }
          smoothedEAR /= earBuffer.Count;
        }
      }
    }

    // Method to calculate EAR (Eye Aspect Ratio)
    private float CalculateEAR(Vector3[] eyeLandmarks)
    {
      float vertical1 = Vector3.Distance(eyeLandmarks[1], eyeLandmarks[5]);
      float vertical2 = Vector3.Distance(eyeLandmarks[2], eyeLandmarks[4]);
      float horizontal = Vector3.Distance(eyeLandmarks[0], eyeLandmarks[3]);

      return (vertical1 + vertical2) / (2.0f * horizontal);
    }

    private void Update()
    {
      if (blinkTimer > 0)
      {
        blinkTimer -= Time.deltaTime;  // Decrease cooldown timer
      }

      // Detect blink based on smoothed EAR and cooldown
      if (smoothedEAR < EAR_THRESHOLD && !isBlinking && blinkTimer <= 0)
      {
        if (debugUI != null)
        {
          debugUI.UpdateBlinkStatus("Eyes Blinked");  // Update text to "Eyes Blinked"
        }

        isBlinking = true;  // Blink has started
        blinkTimer = blinkCooldown;  // Reset blink cooldown
      }
      else if (smoothedEAR >= EAR_THRESHOLD && isBlinking && blinkTimer <= 0)
      {
        if (debugUI != null)
        {
          debugUI.UpdateBlinkStatus("Eyes Open");  // Update text to "Eyes Open"
        }

        isBlinking = false;  // Blink has ended
      }
    }
  }
}
