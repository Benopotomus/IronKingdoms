using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace FOW
{
    [DefaultExecutionOrder(-50)]
    public class MiniMapIconManager : MonoBehaviour
    {
        public static MiniMapIconManager instance;

        [Header("References")]
        public Image MapImageComponent;
        public Image IconPrefab;

        [Header("Optimization")]
        [Tooltip("The maximum number of icons you expect to register. This will auto expand if needed.")]
        public int InitialMaxCapacity = 64;

        private Dictionary<Transform, Image> activeIcons;
        private Stack<Image> pooledIcons;

        private void Awake()
        {
            if (instance != null)
                return;
            instance = this;

            activeIcons = new Dictionary<Transform, Image>(capacity: InitialMaxCapacity);
            pooledIcons = new Stack<Image>(capacity: InitialMaxCapacity);
            for (int i = 0; i < pooledIcons.Count; i++)
                pooledIcons.Push(CreateNewImage());
        }

        private void OnDestroy()
        {
            if (instance != this)
                return;
            instance = null;
        }

        private void Update()
        {
            RectTransform parentRect = MapImageComponent.GetComponent<RectTransform>();
            foreach (var icon in activeIcons)
            {
                Vector2 uv = FogOfWarWorld.GetFowTextureUVFromWorldPosition(icon.Key.position);

                Vector2 localPos = new Vector2(
                    (uv.x - parentRect.pivot.x) * parentRect.rect.width,
                    (uv.y - parentRect.pivot.y) * parentRect.rect.height
                );

                icon.Value.transform.localPosition = localPos;
            }    
        }

        #region image pooling

        private Image CreateNewImage()
        {
            Image newImage = Instantiate(IconPrefab, MapImageComponent.transform);
            newImage.gameObject.SetActive(false);
            newImage.transform.rotation = Quaternion.identity;
            newImage.transform.localScale = Vector3.one;
            return newImage;
        }

        private Image PullImageFromPool()
        {
            if (pooledIcons.Count == 0)
                pooledIcons.Push(CreateNewImage());
            return pooledIcons.Pop();
        }

        #endregion

        public void TrackNewObject(Transform tracker, float scale, Color color, Sprite sprite)
        {
            Image image = PullImageFromPool();

            image.gameObject.SetActive(true);
            image.transform.localScale = Vector3.one * scale;
            image.color = color;
            image.sprite = sprite;

            activeIcons.Add(tracker, image);
        }

        public void StopTrackingObject(Transform tracker)
        {
            if (!activeIcons.TryGetValue(tracker, out Image image))
                return;

            image.gameObject.SetActive(false);
            pooledIcons.Push(image);
            activeIcons.Remove(tracker);
        }
    }
}
