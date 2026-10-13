# A3Print

Imprime à la chaîne les PDF du jour du dossier Téléchargements dont le nom commence par `FA` ou `AV`.
Windows limite l'impression groupée à une quinzaine de fichiers ; A3Print en envoie plusieurs centaines,
un par un, dans l'ordre.

## Utilisation

1. Lancer A3Print : la liste des PDF `FA*` / `AV*` téléchargés aujourd'hui s'affiche, tous cochés.
   Le sélecteur « Documents du » affiche un autre jour, le bouton **Aujourd'hui** revient au jour courant.
2. Décocher ce qui ne doit pas sortir, choisir l'imprimante (mémorisée ensuite), copies et recto-verso.
3. **Imprimer**, puis confirmer.

Les documents déjà envoyés sont retenus : à la réouverture ils apparaissent décochés, marqués
« Déjà envoyé ». Les documents en échec restent cochés, un second clic sur **Imprimer** les renvoie.

## Coupures et erreurs

- **Imprimante hors ligne, sans papier, en bourrage** : le lot se met en pause et reprend tout seul
  quand l'imprimante répond. L'état vient de Windows ; selon le pilote, une panne peut ne pas remonter.
- **Trois échecs de suite** : pause, avec le choix de reprendre ou d'arrêter.
- **Fichier vide, verrouillé ou qui n'est pas un PDF** : écarté avec la raison dans la colonne État.
- **Fermeture, plantage ou coupure de courant en plein lot** : à la réouverture un bandeau le signale.
  Les documents envoyés sont décochés, les autres restent cochés. Le document qui partait au moment de
  la coupure est marqué « À vérifier » et décoché, pour éviter un doublon.
- **Mise à jour coupée pendant le téléchargement** : trois essais, puis un message avec **Réessayer**.
  Rien n'est installé tant que le fichier n'est pas complet et contrôlé.
- Le lien **Journal** en bas de la fenêtre ouvre l'historique détaillé (`%LOCALAPPDATA%\A3Print\a3print.log`).

Options de ligne de commande, pour les essais :

- `--dry-run` : simulation, rien n'est envoyé à l'imprimante ;
- `--folder <chemin>` : analyser un autre dossier que Téléchargements.

## Installation, désinstallation

`A3Print-Setup-<version>.exe` s'installe pour l'utilisateur courant, sans droits administrateur.
Désinstallation depuis Paramètres > Applications > Applications installées (ou Panneau de configuration >
Programmes et fonctionnalités).

L'installeur n'est pas signé : SmartScreen peut afficher un avertissement
(« Informations complémentaires » > « Exécuter quand même »).

## Mises à jour

Au lancement, l'application interroge la dernière release GitHub. Si elle est plus récente, elle propose
de l'installer : téléchargement, contrôle SHA-256, installation silencieuse, relance.

Publier une nouvelle version :

1. changer les deux numéros de version dans `src/AssemblyInfo.cs` ;
2. commit ;
3. `.\release.ps1` (compile, pousse, crée la release `vX.Y.Z` avec l'installeur et son empreinte).

## Compilation

```powershell
.\build.ps1
```

Prérequis : Inno Setup 6 (`winget install JRSoftware.InnoSetup`). Le compilateur C# utilisé est celui
livré avec Windows (.NET Framework 4.8), d'où un code limité à la syntaxe C# 5.
Résultat : `dist\A3Print-Setup-<version>.exe` et son `.sha256`.

## Licences

L'impression des PDF est assurée par [SumatraPDF](https://www.sumatrapdfreader.org/) (GPLv3),
distribué tel quel à côté de l'application.
