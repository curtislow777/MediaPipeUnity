// Copyright (c) 2023 homuler
//
// Use of this source code is governed by an MIT-style
// license that can be found in the LICENSE file or at
// https://opensource.org/licenses/MIT.

using System.Collections;
using System.Collections.Generic;
using System.Security.Cryptography;
using Mediapipe.Tasks.Vision.HandLandmarker;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

namespace Mediapipe.Unity.Sample.HandLandmarkDetection
{
  public class HandLandmarkerRunner : VisionTaskApiRunner<HandLandmarker>
  {
    [SerializeField] private HandLandmarkerResultAnnotationController _handLandmarkerResultAnnotationController;

    private Experimental.TextureFramePool _textureFramePool;

    public readonly HandLandmarkDetectionConfig config = new HandLandmarkDetectionConfig();

    public DebugUI debugUI;

    public GameObject testModel;




    public Transform wristBone, wrist1Bone, wrist2Bone, wrist3Bone, wrist4Bone, wrist5Bone;
    public Transform thumb0Bone, thumb1Bone, thumb2Bone, thumb3Bone;
    public Transform index0Bone, index1Bone, index2Bone, index3Bone, index4Bone;
    public Transform middle0Bone, middle1Bone, middle2Bone, middle3Bone, middle4Bone;
    public Transform ring0Bone, ring1Bone, ring2Bone, ring3Bone, ring4Bone;
    public Transform pinky0Bone, pinky1Bone, pinky2Bone, pinky3Bone, pinky4Bone;


    private Vector3[] handLandmarksWorldPositions;


    private Dictionary<string, bool> GetIndividualFingerStates(HandLandmarkerResult result, int handIndex)
    {
      var fingersUp = new Dictionary<string, bool>
    {
        { "Thumb", false },
        { "Index", false },
        { "Middle", false },
        { "Ring", false },
        { "Pinky", false }
    };

      // Access the landmarks for the specified hand
      var handLandmarks = result.handLandmarks[handIndex].landmarks;

      // Check for "Left" or "Right" hand
      string handedness = result.handedness[handIndex].categories[0].categoryName; // "Left" or "Right"

      // Thumb logic
      if (handedness == "Left")
      {
        fingersUp["Thumb"] = handLandmarks[4].x < handLandmarks[2].x; // Left hand thumb logic
      }
      else if (handedness == "Right")
      {
        fingersUp["Thumb"] = handLandmarks[4].x > handLandmarks[2].x; // Right hand thumb logic
      }


      fingersUp["Index"] = handLandmarks[8].y < handLandmarks[5].y;

      fingersUp["Middle"] = handLandmarks[12].y < handLandmarks[9].y;

      fingersUp["Ring"] = handLandmarks[16].y < handLandmarks[13].y;

      fingersUp["Pinky"] = handLandmarks[20].y < handLandmarks[17].y;

      return fingersUp;
    }


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
      Debug.Log($"NumHands = {config.NumHands}");
      Debug.Log($"MinHandDetectionConfidence = {config.MinHandDetectionConfidence}");
      Debug.Log($"MinHandPresenceConfidence = {config.MinHandPresenceConfidence}");
      Debug.Log($"MinTrackingConfidence = {config.MinTrackingConfidence}");

      yield return AssetLoader.PrepareAssetAsync(config.ModelPath);

      var options = config.GetHandLandmarkerOptions(config.RunningMode == Tasks.Vision.Core.RunningMode.LIVE_STREAM ? OnHandLandmarkDetectionOutput : null);
      taskApi = HandLandmarker.CreateFromOptions(options, GpuManager.GpuResources);
      var imageSource = ImageSourceProvider.ImageSource;

      yield return imageSource.Play();

      if (!imageSource.isPrepared)
      {
        Debug.LogError("Failed to start ImageSource, exiting...");
        yield break;
      }

      // Use RGBA32 as the input format.
      // TODO: When using GpuBuffer, MediaPipe assumes that the input format is BGRA, so maybe the following code needs to be fixed.
      _textureFramePool = new Experimental.TextureFramePool(imageSource.textureWidth, imageSource.textureHeight, TextureFormat.RGBA32, 10);

      // NOTE: The screen will be resized later, keeping the aspect ratio.
      screen.Initialize(imageSource);

      SetupAnnotationController(_handLandmarkerResultAnnotationController, imageSource);

      var transformationOptions = imageSource.GetTransformationOptions();
      var flipHorizontally = transformationOptions.flipHorizontally;
      var flipVertically = transformationOptions.flipVertically;
      var imageProcessingOptions = new Tasks.Vision.Core.ImageProcessingOptions(rotationDegrees: (int)transformationOptions.rotationAngle);

      AsyncGPUReadbackRequest req = default;
      var waitUntilReqDone = new WaitUntil(() => req.done);
      var result = HandLandmarkerResult.Alloc(options.numHands);

      // NOTE: we can share the GL context of the render thread with MediaPipe (for now, only on Android)
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

        // Build the input Image
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
              _handLandmarkerResultAnnotationController.DrawNow(result);
            }
            else
            {
              _handLandmarkerResultAnnotationController.DrawNow(default);
            }
            break;
          case Tasks.Vision.Core.RunningMode.VIDEO:
            if (taskApi.TryDetectForVideo(image, GetCurrentTimestampMillisec(), imageProcessingOptions, ref result))
            {
              _handLandmarkerResultAnnotationController.DrawNow(result);
            }
            else
            {
              _handLandmarkerResultAnnotationController.DrawNow(default);
            }
            break;
          case Tasks.Vision.Core.RunningMode.LIVE_STREAM:
            taskApi.DetectAsync(image, GetCurrentTimestampMillisec(), imageProcessingOptions);
            break;
        }
      }
    }

private void OnHandLandmarkDetectionOutput(HandLandmarkerResult result, Image image, long timestamp)
{

    if (image == null)
    {
        Debug.LogWarning("Image is null.");
        return;
    }

    if (result.handLandmarks == null || result.handLandmarks.Count == 0)
    {
        debugUI.UpdateStatus("No hand landmarks detected.");
        debugUI.UpdateHandStatus("No hands are up."); 
        return;
    }



    // Update the pose based on the detected landmarks
    for (int handIndex = 0; handIndex < result.handLandmarks.Count; handIndex++)
    {
        var handLandmarks = result.handLandmarks[handIndex].landmarks;

        // Enqueue the UpdatePose action to be executed on the main thread
        MainThreadDispatcher.Enqueue(() => UpdatePose(handLandmarks));
    }






      Debug.Log("Hand landmarks detected, proceeding to iterate over them...");




    int totalFingersCount = 0;
    bool leftHandUp = false;
    bool rightHandUp = false;

    // Prepare a message for displaying finger statuses for both hands
    string combinedFingerStatus = "";

    for (int handIndex = 0; handIndex < result.handLandmarks.Count; handIndex++)
    {




        // Get the state of each finger for this hand
        var fingersUp = GetIndividualFingerStates(result, handIndex);

        // Count how many fingers are up for this hand
        int fingersCount = 0;
        foreach (var finger in fingersUp)
        {
            if (finger.Value)
                fingersCount++;
        }

        totalFingersCount += fingersCount; // Keep a total count of fingers up

        // Prepare a message to display which fingers are up
        string fingerStatus = $"Hand {handIndex + 1} ({result.handedness[handIndex].categories[0].categoryName}): ";
        foreach (var finger in fingersUp)
        {
            if (finger.Value)
            {
                fingerStatus += $"{finger.Key}, "; // Add to the status if the finger is up
            }
        }

        // Trim the last comma and space
        fingerStatus = fingerStatus.TrimEnd(',', ' ');

        // Combine finger status messages for both hands
        combinedFingerStatus += fingerStatus + "\n";

        // Determine hand status
        if (result.handedness[handIndex].categories[0].categoryName == "Left")
        {
            leftHandUp = fingersCount > 0; // Set flag if left hand is up
        }
        else if (result.handedness[handIndex].categories[0].categoryName == "Right")
        {
            rightHandUp = fingersCount > 0; // Set flag if right hand is up
        }

        Debug.Log(fingerStatus);
    }

    // Update total count message
    string countMessage = $"Total fingers up: {totalFingersCount}.";
    debugUI.UpdateStatus(countMessage); // Update the existing UI with the count

    // Update hand status message
    string handStatusMessage;
    if (leftHandUp && rightHandUp)
    {
        handStatusMessage = "Both hands are up.";
    }
    else if (leftHandUp)
    {
        handStatusMessage = "Left hand is up.";
    }
    else if (rightHandUp)
    {
        handStatusMessage = "Right hand is up.";
    }
    else
    {
        handStatusMessage = "No hands are up.";
    }

    debugUI.UpdateHandStatus(handStatusMessage); // Update the new text element for hand status
    
    // Update the finger status text for both hands
    debugUI.UpdateFingerStatus(combinedFingerStatus.TrimEnd('\n')); // Show which fingers are up for both hands

    _handLandmarkerResultAnnotationController.DrawLater(result);
}




    private int CountFingers(HandLandmarkerResult result, int handIndex)
    {
      int count = 0;

      // Access the landmarks for the specified hand
      var handLandmarks = result.handLandmarks[handIndex].landmarks;
      // Determine handedness (assuming result.handedness is correctly populated)
      string handedness = result.handedness[handIndex].categories[0].categoryName; // "Left" or "Right"
      Debug.Log(handedness);

      // Thumb logic
      if (handedness == "Left")
      {
        // Left hand: Tip (4) should be to the left of base (2) when extended
        if (handLandmarks[4].x < handLandmarks[2].x) count++; // Extended if tip is more to the left (lower x value)
      }
      else if (handedness == "Right")
      {
        // Right hand: Tip (4) should be to the right of base (2) when extended
        if (handLandmarks[4].x > handLandmarks[2].x) count++; // Extended if tip is more to the right (higher x value)
      }

      // Index Finger (indices 8 and 5)
      if (handLandmarks[8].y < handLandmarks[5].y) count++;

      // Middle Finger (indices 12 and 9)
      if (handLandmarks[12].y < handLandmarks[9].y) count++;

      // Ring Finger (indices 16 and 13)
      if (handLandmarks[16].y < handLandmarks[13].y) count++;

      // Pinky Finger (indices 20 and 17)
      if (handLandmarks[20].y < handLandmarks[17].y) count++;

      if (count == 0)
      {
        Debug.Log("No fingers are up.");
      }

      return count;
    }





    private void UpdatePose(List<Mediapipe.Tasks.Components.Containers.NormalizedLandmark> landmarks)
    {
      if (landmarks == null || landmarks.Count < 21)
        return;

      float scaleFactor = 10f; // Adjust this according to your model's size

      Vector3[] landmarks_world = new Vector3[landmarks.Count];
      for (int i = 0; i < landmarks.Count; i++)
      {
        // Map MediaPipe coordinates to Unity coordinates with scaling and flipping Y
        landmarks_world[i] = new Vector3(
            landmarks[i].x * scaleFactor,            // Scale X
            (1 - landmarks[i].y) * scaleFactor,      // Invert and scale Y
            landmarks[i].z * scaleFactor             // Invert and scale Z
        );
      }

      // Set the wrist position directly from the wrist landmark (index 0)
      wrist1Bone.position = landmarks_world[0]; // Ensure this is the wrist landmark
      wrist2Bone.position = landmarks_world[0]; // Ensure this is the wrist landmark
      wrist3Bone.position = landmarks_world[0]; // Ensure this is the wrist landmark
      wrist4Bone.position = landmarks_world[0]; // Ensure this is the wrist landmark
      wrist5Bone.position = landmarks_world[0]; // Ensure this is the wrist landmark

      // Set finger bone positions using direct indices
      SetFingerBonePositions(index0Bone, index1Bone, index2Bone, index3Bone, index4Bone, 1, 2, 3, 4, landmarks_world);
      SetFingerBonePositions(middle0Bone, middle1Bone, middle2Bone, middle3Bone, middle4Bone, 5, 6, 7, 8, landmarks_world);
      SetFingerBonePositions(ring0Bone, ring1Bone, ring2Bone, ring3Bone, ring4Bone, 9, 10, 11, 12, landmarks_world);
      SetFingerBonePositions(pinky0Bone, pinky1Bone, pinky2Bone, pinky3Bone, pinky4Bone, 13, 14, 15, 16, landmarks_world);
      SetThumbBonePositions(thumb0Bone, thumb1Bone, thumb2Bone, thumb3Bone, 17, 18, 19, landmarks_world);
    }


    private void SetFingerBonePositions(
        Transform bone0, // Virtual bone between wrist and base
        Transform bone1, // Base of the finger (metacarpal)
        Transform bone2, // First joint (proximal phalanx)
        Transform bone3, // Second joint (middle phalanx)
        Transform bone4, // Tip of the finger (distal phalanx)
        int baseIndex, // Index for base landmark
        int firstJointIndex, // Index for first joint landmark
        int secondJointIndex, // Index for second joint landmark
        int thirdJointIndex, // Index for third joint landmark
        Vector3[] landmarks_world) // Landmark positions
    {
      // Set the positions for the finger bones directly from the landmarks
      bone0.position = (landmarks_world[0] + landmarks_world[baseIndex]) / 2f; // Midpoint between wrist and base
      bone1.position = landmarks_world[baseIndex]; // Base of the finger
      bone2.position = landmarks_world[firstJointIndex]; // First joint position
      bone3.position = landmarks_world[secondJointIndex]; // Second joint position
      bone4.position = landmarks_world[thirdJointIndex]; // Tip position
    }





    private void SetThumbBonePositions(
        Transform bone0,
        Transform bone1,
        Transform bone2,
        Transform bone3,
        int baseIndex,
        int firstJointIndex,
        int secondJointIndex,
        Vector3[] landmarks_world)
    {
      Vector3 wristPosition = landmarks_world[0];
      Vector3 thumbBasePosition = landmarks_world[baseIndex];
      Vector3 firstJointPosition = landmarks_world[firstJointIndex];
      Vector3 secondJointPosition = landmarks_world[secondJointIndex];

      // Debug log for positions
      Debug.Log($"Thumb Positions - Wrist: {wristPosition}, Base: {thumbBasePosition}, 1st Joint: {firstJointPosition}, 2nd Joint: {secondJointPosition}");

      bone0.position = (wristPosition + thumbBasePosition) / 2f;
      bone1.position = thumbBasePosition;
      bone2.position = firstJointPosition;
      bone3.position = secondJointPosition;
    }

 

  }
}
