using Mediapipe.Unity.Sample.HandLandmarkDetection;
using UnityEngine;

public class WeaponAttachment : MonoBehaviour
{
  public HandLandmarkerRunner handLandmarkerRunner; // Reference to the HandLandmarkerRunner script

  void Update()
  {
    // Access the hand landmarks from the HandLandmarkerRunner
    if (handLandmarkerRunner != null)
    {
      Vector3[] handLandmarks = handLandmarkerRunner.GetHandLandmarksWorldPositions();
      Debug.Log(handLandmarks);
      // Check if the landmarks are available
      if (handLandmarks != null && handLandmarks.Length > 0)
      {
        // Print each landmark's position
        for (int i = 0; i < handLandmarks.Length; i++)
        {
          Debug.Log($"Landmark {i}: Position = {handLandmarks[i]}");
        }
      }
    }
  }
}
