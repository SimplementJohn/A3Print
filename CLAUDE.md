# A3Print

Application Windows (A3 PRO) qui imprime à la chaîne les PDF du dossier Téléchargements dont le nom
commence par `FA` ou `AV`, pour un jour donné (aujourd'hui par défaut). Windows refuse d'imprimer plus
d'une quinzaine de fichiers d'un coup ; A3Print en envoie plusieurs centaines, un par un, dans l'ordre.

Interface, messages, commentaires et README sont en **français**. Les messages de commit sont en anglais.

- Repo : https://github.com/SimplementJohn/A3Print (public)
- Utilisateur du poste de dev : compte GitHub `SimplementJohn`, `gh` déjà connecté.

## Commandes

```powershell
.\build.ps1      # compile build\A3Print.exe puis dist\A3Print-Setup-<version>.exe (+ .sha256)
.\release.ps1    # build + git push + release GitHub v<version> (exige un arbre git propre)
powershell -File tools\make-icon.ps1   # régénère assets\app.ico, seulement si assets\logo.png change
```

Options de l'exe, pour les essais :

- `--dry-run` : simulation, rien n'est envoyé à l'imprimante, aucun historique écrit ;
- `--folder <chemin>` : analyser un autre dossier que Téléchargements.

## Contraintes techniques

- **C# 5 uniquement.** Le projet est compilé avec le `csc.exe` livré dans Windows
  (`%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe`, .NET Framework 4.8). Pas de SDK dotnet, pas de
  `.csproj`. Donc pas de `$"..."`, `?.`, `nameof`, `out var`, membres à corps d'expression, `=>` sur
  propriétés. `async/await`, lambdas et `??` sont disponibles.
- `build.ps1` compile `src\*.cs` : tout nouveau fichier `.cs` dans `src\` est pris automatiquement.
  Une nouvelle référence d'assembly s'ajoute à la ligne `/r:` de `build.ps1`.
- Les scripts `.ps1` et `setup.iss` restent en **ASCII** (pas d'accents) : Windows PowerShell 5.1 lit mal
  l'UTF-8 sans BOM. Les `.cs` sont en UTF-8, compilés avec `/codepage:65001`.
- WinForms sans designer : toute l'interface est construite en code. Pas de mise à l'échelle automatique,
  chaque taille en pixels passe par `S()` (`MainForm`) pour suivre le DPI.
- Installeur : Inno Setup 6 (`winget install JRSoftware.InnoSetup`), installé ici dans
  `%LOCALAPPDATA%\Programs\Inno Setup 6`.
- Moteur d'impression : SumatraPDF portable (GPLv3), téléchargé par `build.ps1` dans `vendor\` et livré
  tel quel à côté de l'exe. Appel : `SumatraPDF.exe -print-to "<imprimante>" -print-settings "..." -silent "<pdf>"`.

## Limite du poste de dev : l'exe ne se lance pas depuis une session Claude

Sur ce PC (Bitdefender Endpoint Security), lancer `A3Print.exe` depuis le shell de Claude renvoie
« Accès refusé » et **l'exe est supprimé** (dossier `build\` comme dossier d'installation). L'utilisateur,
lui, lance l'application sans problème. L'installeur et le désinstalleur se lancent normalement.

Conséquences :

- ne pas tenter de lancer l'exe pour tester ; ne pas chercher à contourner l'antivirus ;
- vérifier par la compilation, puis installer en silencieux et demander à l'utilisateur de tester ou
  d'envoyer une capture d'écran ;
- si un lancement a supprimé l'exe installé, relancer l'installeur pour le restaurer.

Installer la version compilée sur le poste (ferme l'application si elle est ouverte : vérifier d'abord
dans le journal qu'aucun lot n'est en cours) :

```powershell
Start-Process dist\A3Print-Setup-<version>.exe -ArgumentList '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART' -Wait
```

## Arborescence

| Chemin | Rôle |
|---|---|
| `src/Program.cs` | Point d'entrée, arguments, instance unique (mutex), gestion globale des erreurs |
| `src/MainForm.cs` | Toute la fenêtre : construction de l'interface, liste, lot d'impression, mise à jour |
| `src/Controls.cs` | `Theme` (couleurs), `BufferedListView`, `ThinProgress`, `NoticeBar` (bandeau de message) |
| `src/DocumentScanner.cs` | Dossier Téléchargements, filtre `FA`/`AV` + `.pdf` + jour, tri naturel |
| `src/PdfPrinter.cs` | Validation du fichier, appel SumatraPDF, délai, code retour |
| `src/PrinterMonitor.cs` | État de l'imprimante via WMI (`Win32_Printer`) |
| `src/BatchJournal.cs` | Trace sur disque du lot en cours, pour la reprise après coupure |
| `src/Settings.cs` | Préférences, historique des envois, `SafeFile` (écritures atomiques) |
| `src/Updater.cs` | Recherche de release GitHub, téléchargement contrôlé, lancement de l'installeur |
| `src/Log.cs` | Journal texte |
| `src/Native.cs` | Anti-veille, clignotement, progression dans la barre des tâches |
| `src/AssemblyInfo.cs` | **Source unique de la version** |
| `installer/setup.iss` | Script Inno Setup |
| `assets/` | `logo.png` (embarqué en ressource `A3Print.logo.png`), `app.ico` |
| `build/`, `dist/`, `vendor/` | Générés, ignorés par git |

## Fonctionnement

### Liste

`DocumentScanner.Scan(dossier, jour)` retient les `.pdf` dont le nom commence par `FA` ou `AV`
(insensible à la casse) et dont la date de création **ou** de modification tombe le jour choisi.
Le jour se choisit avec ‹ / date (calendrier) / › / Aujourd'hui ; les jours futurs sont interdits.
Tant que le jour affiché est aujourd'hui (`followToday`), la fenêtre suit le changement de date si elle
reste ouverte la nuit.

Chaque document est identifié par `chemin|taille|date de modification` (`Settings.Key`). Un document déjà
envoyé apparaît décoché, « Déjà envoyé ».

### Lot d'impression (`MainForm.Worker_DoWork`, fil de fond)

Pour chaque document coché, dans l'ordre :

1. toutes les 5 pièces, contrôle de l'imprimante (`PrinterMonitor`) ; si indisponible : **pause**, reprise
   automatique à son retour (revérification toutes les 5 s) ou par le bouton Reprendre ;
2. `BatchJournal.Sending` écrit le document « en cours » sur disque ;
3. `PdfPrinter.Print` : validation (existe, non vide, non verrouillé, contient `%PDF`), puis SumatraPDF ;
4. un refus net (code retour ≠ 0) est retenté une fois, ou après le retour de l'imprimante ; un délai
   dépassé (3 min) ne l'est pas, pour ne pas risquer un doublon ;
5. `Settings.MarkPrinted` ajoute le document à l'historique (vidé sur disque), puis `BatchJournal.Sent`
   efface le marqueur « en cours » ;
6. trois échecs de suite : pause, l'utilisateur reprend ou arrête.

« Envoyé » signifie remis à la file d'impression Windows, pas forcément sorti sur papier. L'état WMI est
indicatif : sur une imprimante partagée par le serveur, une panne peut ne pas remonter.

### Reprise après coupure

Fichiers dans `%LOCALAPPDATA%\A3Print\` :

| Fichier | Contenu |
|---|---|
| `settings.ini` | Imprimante, copies, recto-verso |
| `imprimes.txt` | Une clé par document envoyé |
| `lot-en-cours.txt` | Présent tant qu'un lot n'est pas terminé proprement |
| `envoi-en-cours.txt` | Le document en train de partir |
| `a-verifier.txt` | Documents trouvés « en train de partir » à un redémarrage |
| `a3print.log` | Journal (rotation à 1 Mo vers `.old`) |

Au démarrage, si `lot-en-cours.txt` existe : bandeau « Impression interrompue ». Le document de
`envoi-en-cours.txt` est marqué « À vérifier : envoi coupé » et décoché, son sort étant inconnu.
La désinstallation supprime ce dossier.

### Mises à jour

- **Publier** : changer les deux numéros dans `src/AssemblyInfo.cs`, commit, `.\release.ps1`. Pousser du
  code seul ne met rien à jour : seule une release GitHub compte.
- **Recevoir** : à l'ouverture, `Updater.Check` lit `releases/latest`. Si le tag est plus grand que la
  version de l'assembly, un bandeau propose **Installer**. `Updater.Download` télécharge dans un `.part`
  (progression, annulation, trois essais), compare au `.sha256` publié, puis lance l'installeur avec
  `/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /RELAUNCH=1` ; l'installeur relance l'application.
- L'installeur s'appelle `A3Print-Setup-<version>.exe` ; l'updater repère l'asset par le préfixe
  `A3Print-Setup` et les suffixes `.exe` / `.exe.sha256`. Ne pas changer ce préfixe sans adapter `Updater`.
- Installation par utilisateur (`%LOCALAPPDATA%\Programs\A3Print`, `PrivilegesRequired=lowest`) : aucun
  droit administrateur. `AppId=A3Print` : ne pas le changer, sinon les mises à jour créent une seconde
  installation.

Releases publiées à ce jour : `v1.0.0`, `v1.0.1`. La 1.1.0 (reprise après coupure, bandeaux, sélecteur de
jour) est dans le code et installée sur le poste de dev, **pas encore publiée**.

## Conventions d'interface

- Couleurs uniquement via `Theme` (`Controls.cs`) ; les couples texte/fond visent un contraste de 4,5:1.
- Un message à l'utilisateur passe par le bandeau `NoticeBar` (titre, explication, bouton pour s'en
  sortir), pas par une `MessageBox`. Restent en `MessageBox` : la confirmation avant impression et la
  confirmation de fermeture pendant un lot.
- Un état ne repose jamais sur la couleur seule : la colonne État porte toujours un texte.
- Ne jamais présélectionner une imprimante virtuelle (PDF, OneNote, XPS, Fax) : elle ouvrirait une boîte
  « Enregistrer sous » par document.
- Toute écriture de fichier d'état passe par `SafeFile` ; toute erreur attrapée est journalisée avec `Log`.
