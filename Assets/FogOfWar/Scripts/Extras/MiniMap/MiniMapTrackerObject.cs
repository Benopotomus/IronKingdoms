using UnityEngine;

namespace FOW
{
    public class MiniMapTrackerObject : MonoBehaviour
    {
        //this is just an example script for how you can use the minimap object tracking. feel free to throw this simple script away and handle it as you wish.
        public float IconScale = 1f;
        public Color IconColor = Color.white;
        public Sprite IconTexture;

        private void OnEnable()
        {
            if (MiniMapIconManager.instance == null)
            {
                Debug.Log("Couldnt register minimap icon! The minimap icon manager doesnt exist!");
                return;
            }
            MiniMapIconManager.instance.TrackNewObject(transform, IconScale, IconColor, IconTexture);
        }

        private void OnDisable()
        {
            if (MiniMapIconManager.instance == null)
                return;
            MiniMapIconManager.instance.StopTrackingObject(transform);
        }
    }
}

