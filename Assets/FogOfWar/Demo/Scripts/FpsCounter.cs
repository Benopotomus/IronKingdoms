using UnityEngine;
using UnityEngine.UI;

namespace FOW.Demos
{
    public class FpsCounter : MonoBehaviour
    {
        public Text FpsText;
        public Text MsText;
        public float UpdateInterval = 1.0f;

        private int frames;
        private float lastUpdateTime;

        private void Start()
        {
            lastUpdateTime = Time.realtimeSinceStartup;
        }

        private void Update()
        {
            ++frames;
            var currentTime = Time.realtimeSinceStartup;

            if (currentTime > lastUpdateTime + UpdateInterval)
            {
                float fps = frames / (currentTime - lastUpdateTime);
                float ms = 1000.0f / Mathf.Max(fps, 0.00001f);

                FpsText.text = $"FPS: {Mathf.Round(fps)}";
                MsText.text = $"{ms.ToString("F3")} ms";

                frames = 0;
                lastUpdateTime = currentTime;
            }
        }
    }
}