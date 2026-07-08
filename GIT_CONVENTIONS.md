# Conventions Git - PixelHub

Afin de garder un historique Git propre et de faciliter le suivi des développements sur le projet, veuillez respecter les instructions et règles suivantes :

## 📌 1. Nommage des branches

Toutes les branches de développement doivent impérativement respecter l'un des formats suivants :

- **Nouvelle fonctionnalité :** `feat/<numéro-jira>-<nom-de-la-branche>`
- **Correction de bug :** `fix/<numéro-jira>-<nom-de-la-branche>`

**Règles pour le `<nom-de-la-branche>` :**
- Le nom doit être explicite et concis.
- Écrit tout en minuscules.
- Les mots doivent être séparés uniquement par des tirets (`-`).

**Exemple valide :**
`feat/1-initialisation-architecture-unity`

## 📌 2. Stratégie de Merge

- Toutes les Pull Requests (ou merges) doivent être effectuées **uniquement vers la branche `develop`**.
- ⚠️ **Il est strictement interdit** de merger directement vers la branche `main` (ou toute autre branche principale), sauf indication contraire explicite du responsable technique.
