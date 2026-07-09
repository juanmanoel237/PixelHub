using System;
using UnityEngine;

namespace Laps.Core
{
    [Serializable]
    public class RouterFilesConfig
    {
        public string entityMappingCsv; // ex: "mapping/led_wall_mapping.csv"
    }

    [Serializable]
    public class RouterConfig
    {
        public RouterFilesConfig files;
    }

    // Extension légère sans casser AppConfig existant: on l’imbrique dans JSON sous "router"
    [Serializable]
    public class ExtendedAppConfig : AppConfig
    {
        public RouterConfig router;
    }
}