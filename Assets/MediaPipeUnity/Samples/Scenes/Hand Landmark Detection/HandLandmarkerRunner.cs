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

    public Transform wristBone;
    public Transform thumb1Bone, thumb2Bone, thumb3Bone, thumb4Bone;
    public Transform index1Bone, index2Bone, index3Bone, index4Bone;
    public Transform middle1Bone, middle2Bone, middle3Bone, middle4Bone;
    public Transform ring1Bone, ring2Bone, ring3Bone, ring4Bone;
    public Transform pinky1Bone, pinky2Bone, pinky3Bone, pinky4Bone;


    private List<List<Vector3>> handLandmarkPositions = new List<List<Vector3>>();
    private List<List<Vector3>> handLandmarkPositionsCopy = new List<List<Vector3>>(); // Copy for Gizmos


    public float xyScaleFactor;
    public float zScaleFactor;
    public float wristOffset = -10f; // To adjust wrist depth position if needed
    public float rotationSpeed = 10f; 


    // Weapon transform to attach
    public Transform weaponTransform;
    public Vector3 weaponPositionOffset = Vector3.zero;  // Adjust if the weapon needs to be offset
    public Vector3 weaponRotationOffset = Vector3.zero;  // Adjust for rotational offset


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


      // Store positions for bones and gizmos
      List<Vector3> landmarks = new List<Vector3>();

      // Convert landmarks from normalized MediaPipe coordinates to Unity space
      foreach (var landmark in result.handLandmarks[0].landmarks) // assuming single hand for now
      {
        Vector3 unityPosition = NormalizeToUnity(landmark.x, landmark.y, landmark.z);
        landmarks.Add(unityPosition);
      }

      // Apply the landmarks to the hand bones
      UpdatePose(landmarks);

      // Store the landmarks for Gizmos drawing
      lock (handLandmarkPositions)
      {
        handLandmarkPositions.Clear(); // Clear the old landmarks
        handLandmarkPositions.Add(landmarks); // Add the new landmarks
      }

    // Ensure we have enough landmarks
    if (landmarks.Count >= 21)
    {
        // Extract key points
        Vector3 C = landmarks[0];  // Wrist
        Vector3 D = landmarks[17]; // Pinky base
        Vector3 A = landmarks[5];  // Index base
        Vector3 B = landmarks[9];  // Middle base

        // Call AttachWeapon with these key landmarks
        AttachWeapon(A, B, C, D);
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

        //Debug.Log(fingerStatus);
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




    private Vector3 NormalizeToUnity(float x, float y, float z)
    {
      float unityX = x * xyScaleFactor;  // X-axis scaling
      float unityZ = -z * zScaleFactor;  // Adjust the Z-axis, try not inverting it
      float unityY = (1 - y) * xyScaleFactor;  // Invert the Y-axis

      return new Vector3(unityX, unityY, unityZ);
    }






    // Draw spheres at each hand landmark position for debugging
    private void OnDrawGizmos()
    {
      // Create a copy of the list before iterating
      lock (handLandmarkPositions)
      {
        handLandmarkPositionsCopy = new List<List<Vector3>>(handLandmarkPositions);
      }

      if (handLandmarkPositionsCopy.Count == 0) return;

      Gizmos.color = UnityEngine.Color.red;

      foreach (var handLandmarks in handLandmarkPositionsCopy)
      {
        foreach (var landmark in handLandmarks)
        {
          Debug.Log($"Landmark Position: {landmark}");  // Log the position to check coordinates
          Gizmos.DrawSphere(landmark, 0.05f); // Increase the size for better visibility
        }
      }
    }



    // Function to update the pose (apply hand landmarks to bones)
    public void UpdatePose(List<Vector3> landmarks)
    {
      if (landmarks == null || landmarks.Count < 21)
        return;

      // Use the dispatcher to ensure position updates happen on the main thread
      MainThreadDispatcher.Enqueue(() =>
      {
        // Map the landmarks to the bones
        wristBone.position = NormalizeToUnity(landmarks[0].x, landmarks[0].y, landmarks[0].z);

        // Thumb
        thumb1Bone.position = NormalizeToUnity(landmarks[1].x, landmarks[1].y, landmarks[1].z);
        thumb2Bone.position = NormalizeToUnity(landmarks[2].x, landmarks[2].y, landmarks[2].z);
        thumb3Bone.position = NormalizeToUnity(landmarks[3].x, landmarks[3].y, landmarks[3].z);
        thumb4Bone.position = NormalizeToUnity(landmarks[4].x, landmarks[4].y, landmarks[4].z);

        // Index Finger
        index1Bone.position = NormalizeToUnity(landmarks[5].x, landmarks[5].y, landmarks[5].z);
        index2Bone.position = NormalizeToUnity(landmarks[6].x, landmarks[6].y, landmarks[6].z);
        index3Bone.position = NormalizeToUnity(landmarks[7].x, landmarks[7].y, landmarks[7].z);
        index4Bone.position = NormalizeToUnity(landmarks[8].x, landmarks[8].y, landmarks[8].z);

        // Middle Finger
        middle1Bone.position = NormalizeToUnity(landmarks[9].x, landmarks[9].y, landmarks[9].z);
        middle2Bone.position = NormalizeToUnity(landmarks[10].x, landmarks[10].y, landmarks[10].z);
        middle3Bone.position = NormalizeToUnity(landmarks[11].x, landmarks[11].y, landmarks[11].z);
        middle4Bone.position = NormalizeToUnity(landmarks[12].x, landmarks[12].y, landmarks[12].z);

        // Ring Finger
        ring1Bone.position = NormalizeToUnity(landmarks[13].x, landmarks[13].y, landmarks[13].z);
        ring2Bone.position = NormalizeToUnity(landmarks[14].x, landmarks[14].y, landmarks[14].z);
        ring3Bone.position = NormalizeToUnity(landmarks[15].x, landmarks[15].y, landmarks[15].z);
        ring4Bone.position = NormalizeToUnity(landmarks[16].x, landmarks[16].y, landmarks[16].z);

        // Pinky Finger
        pinky1Bone.position = NormalizeToUnity(landmarks[17].x, landmarks[17].y, landmarks[17].z);
        pinky2Bone.position = NormalizeToUnity(landmarks[18].x, landmarks[18].y, landmarks[18].z);
        pinky3Bone.position = NormalizeToUnity(landmarks[19].x, landmarks[19].y, landmarks[19].z);
        pinky4Bone.position = NormalizeToUnity(landmarks[20].x, landmarks[20].y, landmarks[20].z);
      });
    }

    private void AttachWeapon(Vector3 A, Vector3 B, Vector3 C, Vector3 D)
    {
      // Calculate midpoints
      Vector3 K = (A + B) / 2f; // Midpoint between 5 and 9
      Vector3 V = (C + D) / 2f; // Midpoint between 0 and 17

      // Calculate normal vectors
      Vector3 N1 = Vector3.Cross(C - D, C - K).normalized;  // Normal to the palm
      Vector3 Y = (A + B + K) / 3f;  // Midpoint of the triangle formed by A, B, and K
      Vector3 N2 = (V - Y).normalized;  // Second normal for full rotation

      // Apply rotation to the weapon based on these vectors
      Quaternion targetRotation = Quaternion.LookRotation(N2, N1);  // N2 for forward, N1 for up

      // Position and rotate the weapon
      MainThreadDispatcher.Enqueue(() =>
      {
        weaponTransform.position = V + weaponPositionOffset; // Attach at V, with optional offset
        weaponTransform.rotation = Quaternion.Slerp(weaponTransform.rotation, targetRotation * Quaternion.Euler(weaponRotationOffset), Time.deltaTime * rotationSpeed);
      });
    }



  }
}
