using UnityEngine;
using System.Collections.Generic;

namespace UnityChan
{
    public class SpringManager : MonoBehaviour
    {
        [Header("Master Switch (Multiplies all below)")]
        [Range(0f, 1f)] public float dynamicRatio = 1.0f;

        [Header("Hair Settings")]
        [Range(0f, 1f)] public float hairDynamicRatio = 1.0f;
        public float hairStiffnessForce = 0.01f;
        public AnimationCurve hairStiffnessCurve = new AnimationCurve(new Keyframe(0, 1), new Keyframe(1, 1));
        public float hairDragForce = 0.4f;
        public AnimationCurve hairDragCurve = new AnimationCurve(new Keyframe(0, 1), new Keyframe(1, 1));
        public SpringBone[] hairBones;

        [Header("Cloth 1 Settings")]
        [Range(0f, 1f)] public float cloth1DynamicRatio = 1.0f;
        public float cloth1StiffnessForce = 0.01f;
        public AnimationCurve cloth1StiffnessCurve = new AnimationCurve(new Keyframe(0, 1), new Keyframe(1, 1));
        public float cloth1DragForce = 0.4f;
        public AnimationCurve cloth1DragCurve = new AnimationCurve(new Keyframe(0, 1), new Keyframe(1, 1));
        public SpringBone[] cloth1Bones;

        [Header("Cloth 2 Settings")]
        [Range(0f, 1f)] public float cloth2DynamicRatio = 1.0f;
        public float cloth2StiffnessForce = 0.01f;
        public AnimationCurve cloth2StiffnessCurve = new AnimationCurve(new Keyframe(0, 1), new Keyframe(1, 1));
        public float cloth2DragForce = 0.4f;
        public AnimationCurve cloth2DragCurve = new AnimationCurve(new Keyframe(0, 1), new Keyframe(1, 1));
        public SpringBone[] cloth2Bones;

        [Header("Cloth 3 Settings")]
        [Range(0f, 1f)] public float cloth3DynamicRatio = 1.0f;
        public float cloth3StiffnessForce = 0.01f;
        public AnimationCurve cloth3StiffnessCurve = new AnimationCurve(new Keyframe(0, 1), new Keyframe(1, 1));
        public float cloth3DragForce = 0.4f;
        public AnimationCurve cloth3DragCurve = new AnimationCurve(new Keyframe(0, 1), new Keyframe(1, 1));
        public SpringBone[] cloth3Bones;

        [HideInInspector]
        public SpringBone[] springBones;

        void Start() { UpdateParameters(); }

        void Update()
        {
#if UNITY_EDITOR
            if(dynamicRatio >= 1.0f) dynamicRatio = 1.0f;
            else if(dynamicRatio <= 0.0f) dynamicRatio = 0.0f;
            UpdateParameters();
#endif
        }

        private void LateUpdate()
        {
            UpdateGroupSprings(hairBones, hairDynamicRatio);
            UpdateGroupSprings(cloth1Bones, cloth1DynamicRatio);
            UpdateGroupSprings(cloth2Bones, cloth2DynamicRatio);
            UpdateGroupSprings(cloth3Bones, cloth3DynamicRatio);
        }

        private void UpdateGroupSprings(SpringBone[] boneGroup, float groupRatio)
        {
            if (boneGroup == null || groupRatio == 0.0f) return;

            // Multiply by the master switch
            float finalRatio = groupRatio * dynamicRatio;
            if (finalRatio == 0.0f) return;

            for (int i = 0; i < boneGroup.Length; i++)
            {
                if (finalRatio > boneGroup[i].threshold)
                {
                    boneGroup[i].UpdateSpring(finalRatio);
                }
            }
        }

        [ContextMenu("Obtain bones of Zhongli")]
        public void GetAllBones()
        {
            Transform[] transforms = GetComponentsInChildren<Transform>();
            List<SpringBone> allBonesList = new List<SpringBone>();
            List<SpringBone> hairList = new List<SpringBone>();
            List<SpringBone> cloth1List = new List<SpringBone>();
            List<SpringBone> cloth2List = new List<SpringBone>();
            List<SpringBone> cloth3List = new List<SpringBone>();

            foreach (Transform o in transforms)
            {
                if (o.name.StartsWith("Cloth1_") || o.name.StartsWith("Hair_") || o.name.StartsWith("Cloth2_") || o.name.StartsWith("Cloth3_"))
                {
                    if (o.childCount > 0)
                    {
                        SpringBone sb = o.gameObject.GetComponent<SpringBone>();
                        if (sb == null) sb = o.gameObject.AddComponent<SpringBone>();
                        sb.child = o.GetChild(0);
                        sb.boneAxis = new Vector3(0, 1, 0);
                    }
                }
            }

            SpringBone[] foundBones = GetComponentsInChildren<SpringBone>();
            foreach (SpringBone sb in foundBones)
            {
                allBonesList.Add(sb);
                if (sb.name.StartsWith("Hair_")) hairList.Add(sb);
                else if (sb.name.StartsWith("Cloth1_")) cloth1List.Add(sb);
                else if (sb.name.StartsWith("Cloth2_")) cloth2List.Add(sb);
                else if (sb.name.StartsWith("Cloth3_")) cloth3List.Add(sb);
            }

            springBones = allBonesList.ToArray();
            hairBones = hairList.ToArray();
            cloth1Bones = cloth1List.ToArray();
            cloth2Bones = cloth2List.ToArray();
            cloth3Bones = cloth3List.ToArray();
        }

        private void UpdateParameters()
        {
            UpdateGroupParameters(hairBones, "stiffnessForce", hairStiffnessForce, hairStiffnessCurve);
            UpdateGroupParameters(hairBones, "dragForce", hairDragForce, hairDragCurve);
            UpdateGroupParameters(cloth1Bones, "stiffnessForce", cloth1StiffnessForce, cloth1StiffnessCurve);
            UpdateGroupParameters(cloth1Bones, "dragForce", cloth1DragForce, cloth1DragCurve);
            UpdateGroupParameters(cloth2Bones, "stiffnessForce", cloth2StiffnessForce, cloth2StiffnessCurve);
            UpdateGroupParameters(cloth2Bones, "dragForce", cloth2DragForce, cloth2DragCurve);
            UpdateGroupParameters(cloth3Bones, "stiffnessForce", cloth3StiffnessForce, cloth3StiffnessCurve);
            UpdateGroupParameters(cloth3Bones, "dragForce", cloth3DragForce, cloth3DragCurve);
        }

        private void UpdateGroupParameters(SpringBone[] boneGroup, string fieldName, float baseValue, AnimationCurve curve)
        {
            if (boneGroup == null || boneGroup.Length == 0) return;
            var start = curve.keys[0].time;
            var end = curve.keys[curve.length - 1].time;
            var prop = boneGroup[0].GetType().GetField(fieldName, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);

            for (int i = 0; i < boneGroup.Length; i++)
            {
                if (!boneGroup[i].isUseEachBoneForceSettings)
                {
                    float scale = (boneGroup.Length <= 1) ? curve.Evaluate(start) : curve.Evaluate(start + (end - start) * i / (boneGroup.Length - 1));
                    prop.SetValue(boneGroup[i], baseValue * scale);
                }
            }
        }
    }
}