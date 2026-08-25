using UnityEngine;

namespace MinMinMart.AvatarVariant.Editor
{
    /// <summary>
    /// アバタールートを探す。
    ///
    /// ルートかどうかは VRCAvatarDescriptor の有無で判定し、自分自身から親へ遡る。
    /// Inspector と Blueprint ID の監視が同じ探し方をする必要があるので、ここに集約する。
    /// </summary>
    internal static class AvatarRootFinder
    {
        /// <summary>
        /// <paramref name="from"/> から親へ遡って最初に見つかったアバタールート。無ければ null。
        /// </summary>
        internal static Transform Find(Transform from)
        {
            Transform t = from;
            while (t != null)
            {
                if (t.GetComponent<VRC.SDK3.Avatars.Components.VRCAvatarDescriptor>() != null)
                {
                    return t;
                }

                t = t.parent;
            }

            return null;
        }

        /// <summary>
        /// アバタールートが持つ PipelineManager。ルートが見つからなければ null。
        /// </summary>
        internal static VRC.Core.PipelineManager FindPipelineManager(Transform from)
        {
            Transform root = Find(from);
            return root != null ? root.GetComponent<VRC.Core.PipelineManager>() : null;
        }
    }
}
