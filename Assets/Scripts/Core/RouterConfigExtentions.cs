using System;
using UnityEngine;

namespace Laps.Core
{
    [Serializable]
    public class ControllerPatchEntry
    {
        public int fromController;
        public int toController;
    }

    [Serializable]
    public class RouterFilesConfig
    {
        public string entityMappingCsv; // ex: "mapping/led_wall_mapping.csv"
    }

    [Serializable]
    public class RouterConfig
    {
        public RouterFilesConfig files;
        public ControllerPatchEntry[] controllerPatch;
    }

    /// <summary>
    /// Extension légère permettant de charger la configuration JSON (router.files, etc.)
    /// sans casser la structure `AppConfig` existante.
    /// Utilisé pour le remapping des contrôleurs et la configuration du routeur.
    /// </summary>
    [Serializable]
    public class ExtendedAppConfig : AppConfig
    {
        public RouterConfig router;
    }
}