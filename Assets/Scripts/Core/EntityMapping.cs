using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace Laps.Core
{
    public struct EntityAddress
    {
        public int controllerIndex;
        public int universe;
        public int channel;
    }

    
    /// <summary>
    /// Composant EntityMapping : Logique spécifique d'authoring/rendu.
    /// </summary>
    public class EntityMapping
    {
        private readonly Dictionary<int, EntityAddress> _map = new Dictionary<int, EntityAddress>(capacity: 20000);

        public int Count => _map.Count;

        public bool TryGet(int entityId, out EntityAddress address) => _map.TryGetValue(entityId, out address);

        public void Clear() => _map.Clear();

        public void LoadFromCsv(string absolutePath)
        {
            _map.Clear();

            if (!File.Exists(absolutePath))
            {
                Debug.LogError($"[EntityMapping] CSV introuvable: {absolutePath}");
                return;
            }

            using var reader = new StreamReader(absolutePath);
            string header = reader.ReadLine(); // entityId,controllerIndex,universe,channel
            if (header == null)
            {
                Debug.LogError($"[EntityMapping] CSV vide: {absolutePath}");
                return;
            }

            int lineNo = 1;
            while (!reader.EndOfStream)
            {
                lineNo++;
                var line = reader.ReadLine();
                if (string.IsNullOrWhiteSpace(line)) continue;

                // Support simple: split par virgule (CSV généré proprement)
                var parts = line.Split(',');
                if (parts.Length < 4)
                {
                    Debug.LogWarning($"[EntityMapping] Ligne {lineNo} invalide: {line}");
                    continue;
                }

                if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int entityId)) continue;
                if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int controllerIndex)) continue;
                if (!int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int universe)) continue;
                if (!int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int channel)) continue;

                _map[entityId] = new EntityAddress
                {
                    controllerIndex = controllerIndex,
                    universe = universe,
                    channel = channel
                };
            }

            Debug.Log($"[EntityMapping] Mapping chargé: {Count} entités depuis {absolutePath}");
        }
    }
}