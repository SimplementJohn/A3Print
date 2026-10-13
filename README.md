# ImprimLot

Imprime à la chaîne les PDF du jour du dossier Téléchargements dont le nom commence par `FA` ou `AV`.
Windows limite l'impression groupée à une quinzaine de fichiers ; ImprimLot en envoie plusieurs centaines,
un par un, dans l'ordre.

## Utilisation

1. Lancer ImprimLot : la liste des PDF `FA*` / `AV*` téléchargés aujourd'hui s'affiche, tous cochés.
2. Décocher ce qui ne doit pas sortir, choisir l'imprimante (mémorisée ensuite), copies et recto-verso.
3. **Imprimer**, puis confirmer.

Les documents déjà imprimés sont retenus : à la réouverture ils apparaissent décochés, marqués
« Déjà imprimé ». Les documents en échec restent cochés, un second clic sur **Imprimer** les renvoie.

Options de ligne de commande, pour les essais :

- `--dry-run` : simulation, rien n'est envoyé à l'imprimante ;
- `--folder <chemin>` : analyser un autre dossier que Téléchargements.

## Installation, désinstallation

`ImprimLot-Setup.exe` s'installe pour l'utilisateur courant, sans droits administrateur.
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
Résultat : `dist\ImprimLot-Setup.exe` et `dist\ImprimLot-Setup.exe.sha256`.

## Licences

L'impression des PDF est assurée par [SumatraPDF](https://www.sumatrapdfreader.org/) (GPLv3),
distribué tel quel à côté de l'application.
