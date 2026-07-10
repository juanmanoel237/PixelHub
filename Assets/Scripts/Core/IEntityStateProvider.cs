using System.Collections.Generic;
using UnityEngine;

namespace Laps.Core
{
    /// <summary>
    /// Fournit un state sous forme de liste d'entités (id → couleur).
    /// C'est la forme naturelle des messages eHuB (P7) et des mappings Excel (IDs non séquentiels).
    /// </summary>
    public interface IEntityStateProvider
    {
        IReadOnlyList<EntityColor> GetEntityState();
    }

    [System.Serializable]
    public struct EntityColor
    {
        public int id;
        public Color32 color;
    }
}

