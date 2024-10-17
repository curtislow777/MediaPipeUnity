using System.Collections;
using UnityEngine;
using TMPro;
using System.Collections.Generic;  

public class DebugUI : MonoBehaviour
{
  public TextMeshProUGUI debugText;

  private Queue<string> _textQueue = new Queue<string>();

  // This will be called to update the text with a custom message
  public void UpdateBlinkStatus(string message)
  {
    _textQueue.Enqueue(message);  // Queue the text update
  }

  private void Update()
  {
    // Only run when there's an item in the queue
    if (_textQueue.Count > 0)
    {
      StartCoroutine(UpdateText(_textQueue.Dequeue()));
    }
  }

  private IEnumerator UpdateText(string newText)
  {
    yield return new WaitForEndOfFrame();  // Wait for the next frame to ensure it's on the main thread
    debugText.text = newText;
  }
}
