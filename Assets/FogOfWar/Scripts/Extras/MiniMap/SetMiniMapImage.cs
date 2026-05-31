using UnityEngine;
using UnityEngine.UI;

namespace FOW.Extras
{
    public class SetMiniMapImage : MonoBehaviour
    {
        public RawImage UIImage;

        private void Start()
        {
            UIImage.texture = FogOfWarWorld.instance.GetFOWRT();
        }
    }
}
