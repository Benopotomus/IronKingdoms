using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Serialization;

namespace FOW
{
    public class HiderRevealer
    {
        public List<FogOfWarHider> HidersSeen = new List<FogOfWarHider>(capacity: 64);

        private BitArray seenBits = new BitArray(1024); //cache the already seen hiders using a fast lookup

        public event Action<FogOfWarHider> OnHiderDeactivated;  //used to send notifications to the revealer for OnHiderVisibilityChanged

        public HiderRevealer()
        {
            OnHiderDeactivated = null;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool ProcessSeen(FogOfWarHider hider, bool seen)     //if the seen state changed, return true. otherwise, return false.
        {
            int id = hider.HiderPermanantID;

            if (id >= seenBits.Length)  //resize when needed.
                seenBits.Length = Mathf.NextPowerOfTwo(id + 1);

            bool wasSeen = seenBits[id];
            if (wasSeen == seen)
                return false;

            seenBits[id] = seen;

            if (seen)
            {
                HidersSeen.Add(hider);
                hider.AddObserver(this);
            }
            else
            {
                RemoveSwapBack(hider);
                hider.RemoveObserver(this);
            }
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RemoveSwapBack(FogOfWarHider hider)    //todo: store "revealer id" as dictionary on hider, to make removal o(1)
        {
            int idx = HidersSeen.IndexOf(hider);
            if (idx < 0) return;

            int last = HidersSeen.Count - 1;
            HidersSeen[idx] = HidersSeen[last];
            HidersSeen.RemoveAt(last);
        }

        public void HiderDeactivated(FogOfWarHider hider)
        {
            RemoveSwapBack(hider);
            seenBits[hider.HiderPermanantID] = false;
            OnHiderDeactivated?.Invoke(hider);
        }

        public void ClearRevealedList()
        {
            foreach (FogOfWarHider hider in HidersSeen)
            {
                if (hider != null)
                {
                    hider.RemoveObserver(this);
                    seenBits[hider.HiderPermanantID] = false;
                }
            }
            HidersSeen.Clear();
        }
    }

    public class FogOfWarHider : MonoBehaviour
    {
        [Tooltip("Leaving this empty will make the hider use its own transform as a sample point.")]
        [FormerlySerializedAs("samplePoints")]
        public Transform[] SamplePoints;
        [Tooltip("If Enabled, the hider will never be hidden again after being revealed once.")]
        public bool PermanentlyReveal = false;

        private float maxSamplePointLocalPosition;
        public float MaxSamplePointLocalPosition => maxSamplePointLocalPosition;

        private int numObservers;
        public int NumObservers => numObservers;

        private List<HiderRevealer> observers = new List<HiderRevealer>(capacity: 64);
        public List<HiderRevealer> CurrentObservers => observers;

        private Transform cachedTransform;
        public Transform CachedTransform => cachedTransform;

        private float2 cachedPosition;
        public float2 CachedPosition => cachedPosition;

        private bool IsRegistered;
        [NonSerialized]
        public int HiderArrayPosition;     //this hiders index in FogOfWarWorld.ActiveHiders. it can change as hiders are added/removed
        [NonSerialized]
        public int HiderPermanantID;     //this hiders index in FogOfWarWorld.UnsortedHiders. this will not change as long as the revealer is alive.

        [NonSerialized] public List<int> SpatialHashBuckets = new List<int>(capacity: 16);
        [NonSerialized] public int2 MinBucket = new int2(int.MinValue, int.MinValue);
        [NonSerialized] public int2 MaxBucket = new int2(int.MinValue, int.MinValue);

        private void OnEnable()
        {
            CalculateSamplePointData();
            RegisterHider();
        }

        private void OnDisable()
        {
            SetActive(true);
            DeregisterHider();
        }

        void CalculateSamplePointData()
        {
            List<Transform> ValidSamplePoints = new List<Transform>();
            for (int i = 0; i < SamplePoints.Length; i++)
            {
                if (SamplePoints[i] != null)
                    ValidSamplePoints.Add(SamplePoints[i]);
            }
            SamplePoints = ValidSamplePoints.ToArray();

            if (SamplePoints.Length == 0)
            {
                SamplePoints = new Transform[1];
                SamplePoints[0] = transform;
            }


            maxSamplePointLocalPosition = 0;
            //for (int i = 0; i < SamplePoints.Length; i++)
            //{
            //    for (int j = i; j < SamplePoints.Length; j++)
            //    {
            //        maxSamplePointLocalPosition = Mathf.Max(maxSamplePointLocalPosition, Vector3.Distance(SamplePoints[i].position, SamplePoints[j].position));
            //    }
            //}
            for (int i = 1; i < SamplePoints.Length; i++)
            {
                float dst = Vector3.Distance(SamplePoints[0].transform.position, SamplePoints[i].transform.position);
                maxSamplePointLocalPosition = math.max(maxSamplePointLocalPosition, dst);
            }
        }

        public void RegisterHider()
        {
            var fow = FogOfWarWorld.instance;
            if (fow == null || fow.IsInPhasedUpdate)
            {
                if (!FogOfWarWorld.PendingHiderDeregister.Remove(this))
                {
                    if (!FogOfWarWorld.PendingHiderRegister.Contains(this))
                        FogOfWarWorld.PendingHiderRegister.Add(this);
                }
                return;
            }
            if (IsRegistered)
            {
                Debug.Log("Tried to double register hider");
                return;
            }

            MinBucket = new int2(int.MinValue, int.MinValue);
            MaxBucket = new int2(int.MinValue, int.MinValue);

            IsRegistered = true;
            cachedTransform = transform;

            HiderPermanantID = FogOfWarWorld.instance.RegisterHider(this);
            SetActive(false);
        }

        public void DeregisterHider()
        {
            var fow = FogOfWarWorld.instance;
            if (fow == null || fow.IsInPhasedUpdate)
            {
                if (!FogOfWarWorld.PendingHiderRegister.Remove(this))
                {
                    if (!FogOfWarWorld.PendingHiderDeregister.Contains(this))
                        FogOfWarWorld.PendingHiderDeregister.Add(this);
                }
                return;
            }
            if (!IsRegistered)
            {
                //Debug.Log("Tried to de-register hider thats not registered");
                return;
            }

            IsRegistered = false;
            FogOfWarWorld.instance.DeRegisterHider(this);
            foreach (HiderRevealer revealer in CurrentObservers)
            {
                revealer.HiderDeactivated(this);
            }
            numObservers = 0;
            CurrentObservers.Clear();
            SparseRevealerGrid.RemoveHider(this);
        }

        public void AddObserver(HiderRevealer Observer)
        {
            CurrentObservers.Add(Observer);
            if (PermanentlyReveal)
            {
                enabled = false;
                return;
            }
            if (NumObservers == 0)
            {
                SetActive(true);
            }
            numObservers++;
        }

        public void RemoveObserver(HiderRevealer Observer)
        {
            CurrentObservers.Remove(Observer);
            numObservers--;
            if (NumObservers == 0)
            {
                SetActive(false);
            }
        }

        public delegate void OnChangeActive(bool isActive);
        public event OnChangeActive OnActiveChanged;
        void SetActive(bool isActive)
        {
            OnActiveChanged?.Invoke(isActive);
        }

        public void UpdateBuckets()
        {
            cachedPosition = FogOfWarRevealer3D.Projection.Project(SamplePoints[0].position);
            SparseRevealerGrid.UpdatHiderBuckets(this, cachedPosition);
        }
    }
}