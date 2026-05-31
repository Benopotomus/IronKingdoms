using UnityEngine;

namespace FOW
{
    public class FowImageEffect : FowImageEffectBase
    {
        void OnRenderImage(RenderTexture src, RenderTexture dest)
        {
            RenderImage(src, dest);
        }
    }
}