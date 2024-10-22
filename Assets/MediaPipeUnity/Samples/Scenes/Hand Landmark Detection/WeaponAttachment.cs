using Mediapipe.Unity.Sample.HandLandmarkDetection;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class WeaponAttachment : MonoBehaviour
{
  public GameObject weapon; // The weapon GameObject
  public HandLandmarkerRunner handLandmarkerRunner; // Reference to the HandLandmarkerRunner

  void Update()
  {
    // Check if HandLandmarkerRunner is assigned
    if (handLandmarkerRunner == null)
    {
      Debug.LogError("HandLandmarkerRunner reference not assigned in WeaponAttachment!");
      return;
    }



  }
}
