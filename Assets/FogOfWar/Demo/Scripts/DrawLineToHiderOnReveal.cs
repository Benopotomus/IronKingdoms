using System.Collections.Generic;
using UnityEngine;

namespace FOW.Demos
{
    public class DrawLineToHiderOnReveal : MonoBehaviour
    {
        [Header("This example script subscribes to FogOfWarRevealer.OnHiderVisibilityChanged, \nwhich is a callback that can be used to detect when a revealer sees or loses sight of a hider.")]

        public FogOfWarRevealer MyRevealer;
        public LineRenderer TemplateLineRenderer;

        private Dictionary<FogOfWarHider, LineRenderer> HidersLineRenderers;

        private void OnEnable()
        {
            MyRevealer.OnHiderVisibilityChanged += OnHiderVisibilityChanged;
            HidersLineRenderers = new Dictionary<FogOfWarHider, LineRenderer>();
        }

        private void OnDisable()
        {
            MyRevealer.OnHiderVisibilityChanged -= OnHiderVisibilityChanged;
            foreach(var kvp in HidersLineRenderers)
                Destroy(kvp.Value.gameObject);
        }

        public void OnHiderVisibilityChanged(FogOfWarHider hider, bool seen)
        {
            if (!HidersLineRenderers.ContainsKey(hider))
            {
                LineRenderer newLineRenderer = Instantiate(TemplateLineRenderer.gameObject, transform).GetComponent<LineRenderer>();
                HidersLineRenderers.Add(hider, newLineRenderer);
            }

            HidersLineRenderers[hider].gameObject.SetActive(seen);
            UpdateLineRendererPosition(hider, HidersLineRenderers[hider]);
        }

        private void Update()
        {
            //access the revealers stored list of currently seen hiders
            for (int i = 0; i < MyRevealer.HiderSeeker.HidersSeen.Count; i++)
            {
                FogOfWarHider Hider = MyRevealer.HiderSeeker.HidersSeen[i];
                UpdateLineRendererPosition(Hider, HidersLineRenderers[Hider]);
            }
        }

        void UpdateLineRendererPosition(FogOfWarHider Hider, LineRenderer renderer)
        {
            renderer.SetPosition(0, transform.position);
            renderer.SetPosition(1, Hider.transform.position);
        }
    }
}