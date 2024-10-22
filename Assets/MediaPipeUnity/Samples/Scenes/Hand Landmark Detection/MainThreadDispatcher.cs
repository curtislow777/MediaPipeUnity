using System;
using System.Collections.Generic;
using UnityEngine;

public class MainThreadDispatcher : MonoBehaviour
{
  private static MainThreadDispatcher instance;
  private static readonly Queue<Action> actions = new Queue<Action>();

  private void Awake()
  {
    if (instance == null)
    {
      instance = this;
      DontDestroyOnLoad(gameObject);
    }
    else
    {
      Destroy(gameObject);
    }
  }

  private void Update()
  {
    lock (actions)
    {
      while (actions.Count > 0)
      {
        actions.Dequeue().Invoke();
      }
    }
  }

  public static void Enqueue(Action action)
  {
    lock (actions)
    {
      actions.Enqueue(action);
    }
  }
}
