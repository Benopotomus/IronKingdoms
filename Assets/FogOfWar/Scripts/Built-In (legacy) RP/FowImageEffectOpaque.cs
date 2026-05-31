using UnityEngine;

namespace FOW
{
    public class FowImageEffectOpaque : FowImageEffectBase
    {
        [ImageEffectOpaque]
        void OnRenderImage(RenderTexture src, RenderTexture dest)
        {
            RenderImage(src, dest);
        }
    }
}