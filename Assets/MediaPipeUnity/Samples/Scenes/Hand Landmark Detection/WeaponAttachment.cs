using Mediapipe.Unity.Sample.HandLandmarkDetection;
using UnityEngine;

public class WeaponAttachment : MonoBehaviour
{
  public HandLandmarkerRunner handLandmarkerRunner; // Reference to the HandLandmarkerRunner script
  public GameObject weapon; // The weapon GameObject

//  void Update()
//  {
//    // Access the hand landmarks from the HandLandmarkerRunner
//    if (handLandmarkerRunner != null)
//    {
//      Vector3[] handLandmarks = handLandmarkerRunner.GetHandLandmarksWorldPositions();
//      Debug.Log(handLandmarks);
//      // Check if the landmarks are available
//      if (handLandmarks != null && handLandmarks.Length > 0)
//      {
//        // Print each landmark's position
//        //for (int i = 0; i < handLandmarks.Length; i++)
//        //{
//        //  Debug.Log($"Landmark {i}: Position = {handLandmarks[i]}");
//        //}
//        Vector3 offset = new Vector3(0, 0, 0.1f); // Fine-tune the offset based on your weapon model

//        // Step 1: Attach weapon to wrist (landmark 0)
//        Vector3 wristPosition = handLandmarks[0]; // Wrist landmark
//        weapon.transform.position = handLandmarks[0] + offset;

//        // Step 2: Calculate rotation (align with hand direction)

//        // Use the wrist (landmark 0) and index base (landmark 5) to calculate the forward direction
//        Vector3 forwardDirection = (handLandmarks[5] - handLandmarks[0]).normalized;

//        // Use wrist (landmark 0) and pinky base (landmark 17) to define the up vector
//        Vector3 upDirection = (handLandmarks[17] - handLandmarks[0]).normalized;

//        // Apply the rotation using Quaternion.LookRotation
//        weapon.transform.rotation = Quaternion.LookRotation(forwardDirection, upDirection);
//      }
//    }
//  }
}
