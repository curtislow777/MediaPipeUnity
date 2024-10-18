using Mediapipe.Unity;
using Mediapipe.Unity.Sample.FaceLandmarkDetection;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class FaceMeshOverlay : MonoBehaviour
{
  [SerializeField] private FaceLandmarkerRunner faceLandmarkerRunner; // Reference to the FaceLandmarkerRunner
  [SerializeField] private MeshFilter faceMeshFilter; // The face mesh filter that holds the mesh

  private Mesh faceMesh;
  private List<Vector3> vertextList = new List<Vector3>(); // List of mesh vertices
  private const float meshScale = 100.0f;  // Adjust based on your scene scale

  void Start()
  {
    // Get the face mesh to modify
    faceMesh = faceMeshFilter.mesh;

    if (faceMesh != null)
    {
      vertextList.AddRange(faceMesh.vertices); // Get the mesh vertex list
      Debug.Log($"Face mesh vertices count: {vertextList.Count}");
    }
    else
    {
      Debug.LogError("Face mesh is not assigned or invalid.");
    }
  }

  void Update()
  {
    if (faceLandmarkerRunner != null && faceLandmarkerRunner.CurrentFaceLandmarks != null)
    {
      // Retrieve the face landmarks detected by MediaPipe as Vector3
      var landmarks = faceLandmarkerRunner.CurrentFaceLandmarks;

      // Ensure that the mesh has 468 vertices and MediaPipe has provided 478 landmarks
      if (landmarks.Count == 478 && vertextList.Count == 468)
      {
        UpdateFaceMesh(landmarks);  // Update the mesh with landmarks
      }
      else
      {
        Debug.LogWarning("Mismatch between landmark count and mesh vertices.");
      }
    }
  }

  private List<Vector3> previousLandmarkPositions = new List<Vector3>(); // Store previous positions
  private const float smoothingFactor = 0.5f; // Adjust smoothing amount

  private void UpdateFaceMesh(IList<Vector3> landmarkList)
  {
    // Initialize previous landmark positions list if empty
    if (previousLandmarkPositions.Count == 0)
    {
      previousLandmarkPositions.AddRange(landmarkList);
    }

    // Iterate through the 468 relevant landmarks
    for (var i = 0; i < 468; i++)
    {
      var currentLandmark = landmarkList[i];

      // Smooth landmark positions by averaging current and previous positions
      var smoothedLandmark = Vector3.Lerp(previousLandmarkPositions[i], currentLandmark, smoothingFactor);

      // Update previous landmark positions
      previousLandmarkPositions[i] = smoothedLandmark;

      // Correct orientation by flipping Y-axis and adjusting Z-axis direction
      vertextList[i] = new Vector3(
          meshScale * (smoothedLandmark.x - 0.5f),   // Center and scale X
          meshScale * (0.5f - smoothedLandmark.y),   // Flip Y-axis and center
          -meshScale * smoothedLandmark.z            // Flip Z-axis
      );
    }

    // Apply the updated vertices to the mesh
    faceMesh.SetVertices(vertextList);
    faceMesh.RecalculateBounds();  // Adjust bounds for proper rendering
    faceMesh.RecalculateNormals();  // Update normals for correct shading
    faceMeshFilter.mesh = faceMesh;  // Apply the updated mesh to the filter
  }



  // Optional: Debugging visualization using Gizmos
  void OnDrawGizmos()
  {
    if (faceLandmarkerRunner != null && faceLandmarkerRunner.CurrentFaceLandmarks != null)
    {
      var landmarks = faceLandmarkerRunner.CurrentFaceLandmarks;  // Access the face landmarks

      for (int i = 0; i < landmarks.Count; i++)
      {
        Vector3 landmarkPosition = new Vector3(
            meshScale * (landmarks[i].x - 0.5f),    // Center and scale X
            meshScale * (0.5f - landmarks[i].y),    // Flip Y-axis and center
            -meshScale * landmarks[i].z             // Flip Z-axis to correct forward direction
        );

        Gizmos.color = Color.red;
        Gizmos.DrawSphere(landmarkPosition, 0.2f);  // Increase sphere size if needed
      }
    }
  }
}
