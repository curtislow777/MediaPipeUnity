using System.Collections;
using UnityEngine;
using TMPro;
using System.Collections.Generic;

public class DebugUI : MonoBehaviour
{
  public TextMeshProUGUI debugText;                // For general status messages
  public TextMeshProUGUI fingerStatusText;         // For finger status messages
  public TextMeshProUGUI handStatusText;           // For hand status messages

  private Queue<string> _textQueue = new Queue<string>();
  private Queue<string> _fingerStatusQueue = new Queue<string>(); // Queue for finger statuses
  private Queue<string> _handStatusQueue = new Queue<string>();   // Queue for hand statuses

  // This will be called to update the text with a custom message
  public void UpdateStatus(string message)
  {
    _textQueue.Enqueue(message);  // Queue the text update
  }

  // This will be called to update the finger state text with a custom message
  public void UpdateFingerStatus(string message)
  {
    _fingerStatusQueue.Enqueue(message);  // Queue the finger status update
  }

  // This will be called to update the hand state text with a custom message
  public void UpdateHandStatus(string message)
  {
    _handStatusQueue.Enqueue(message);  // Queue the hand status update
  }

  private void Update()
  {
    // Process general status messages
    if (_textQueue.Count > 0)
    {
      StartCoroutine(UpdateText(_textQueue.Dequeue(), debugText));
    }

    // Process finger status messages
    if (_fingerStatusQueue.Count > 0)
    {
      StartCoroutine(UpdateText(_fingerStatusQueue.Dequeue(), fingerStatusText));
    }

    // Process hand status messages
    if (_handStatusQueue.Count > 0)
    {
      StartCoroutine(UpdateText(_handStatusQueue.Dequeue(), handStatusText));
    }
  }

  private IEnumerator UpdateText(string newText, TextMeshProUGUI targetText)
  {
    yield return new WaitForEndOfFrame();  // Wait for the next frame to ensure it's on the main thread
    targetText.text = newText;               // Update the specific text element
  }
}
