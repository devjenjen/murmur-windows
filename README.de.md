# Murmur

[English](README.md) | [Deutsch](README.de.md) | [Changelog](CHANGELOG.md)

Murmur ist ein Diktierprogramm für Windows. Push-to-Talk-Taste gedrückt halten, sprechen und loslassen. Murmur formatiert den Text und fügt ihn in die Anwendung mit Fokus ein.

Mit dem Matheprofil kannst du Zahlen und Formeln auf Deutsch oder Englisch einsprechen. Die Sprache der Oberfläche ist dafür unerheblich.

Murmur ist eine eigenständige, KI-unterstützte Windows-Anpassung, inspiriert von [per-simmons/murmur-youtube](https://github.com/per-simmons/murmur-youtube).

## Download und erster Start

Hier gibt es [Murmur 0.2.2 Beta](https://github.com/devjenjen/murmur-windows/releases/tag/v0.2.2) für Windows 11 x64:

- [Setup-EXE](https://github.com/devjenjen/murmur-windows/releases/download/v0.2.2/Murmur-Setup-v0.2.2.exe): installiert Murmur für das aktuelle Benutzerkonto.
- [Portable-ZIP](https://github.com/devjenjen/murmur-windows/releases/download/v0.2.2/Murmur-0.2.2-win-x64.zip): Archiv entpacken und `Murmur.App.exe` starten; es wird nichts installiert.
- `build-info.json` enthält den Quellcode-Commit und Build-Angaben. `SHA256SUMS.txt` enthält die Prüfsummen der Downloads.

Englische Mathediktate sind im aktuellen Quellstand verfügbar. Die Downloads für v0.2.2 verwenden weiterhin nur deutsche Mathe-Regeln.

Der Installer ist nicht signiert. Windows SmartScreen kann deshalb eine Warnung anzeigen. Beim ersten Start öffnet Murmur die Einrichtung. Mikrofon, Taste und Sprachmodell wählen, dann das gewählte Modell herunterladen. Nach der Einrichtung **rechte Strg-Taste** zum Sprechen gedrückt halten und zum Einfügen loslassen. Die Taste lässt sich in den Einstellungen ändern. Murmur bleibt im Infobereich der Taskleiste; über das Tray-Menü öffnest du die Einstellungen oder beendest das Programm.

### Voraussetzungen für Release-Downloads

- Windows 11 x64 und ein funktionierendes Mikrofon; in den Windows-Datenschutzeinstellungen den Mikrofonzugriff für Desktop-Apps erlauben.
- Setup-EXE und Portable-ZIP enthalten die .NET-Runtime, UI-Bibliotheken und native Spracherkennung. .NET SDK, Python oder CUDA müssen nicht installiert werden. Die Erkennung läuft auf der CPU; eine separate Grafikkarte ist nicht erforderlich.
- Für den ersten Modelldownload innerhalb von Murmur ist Internetzugriff auf GitHub nötig. Modellgewichte liegen nicht im Installer. Nach dem Download läuft die Spracherkennung lokal und ohne Internetverbindung; die Updateprüfung beim Start fragt weiterhin GitHub ab.
- Beim Portable-ZIP alle entpackten Dateien zusammenlassen. Die Anwendungs-EXE benötigt die Bibliotheken neben ihr.

Das .NET SDK brauchst du nur beim Kompilieren des Quellcodes. Murmur entpackt Modelle mit dem in Windows 11 enthaltenen `tar.exe`.

Murmur prüft beim Start auf eine neuere stabile Version. Bei einem Treffer öffnet ein Eintrag im Tray-Menü die zugehörige Release-Seite. Updates werden nicht selbst installiert. Beta-Versionen berücksichtigt diese Prüfung nicht; Beta-Updates findest du manuell auf der [Releases-Seite](https://github.com/devjenjen/murmur-windows/releases).

Die Modellarchive sind etwa 465 MiB (Parakeet) und 453 MiB (Nemotron) groß.
Entpackt belegen sie in der lokalen Release-Messung etwa 640 MiB und 653 MiB.
Plane zusätzlichen freien Speicher für Download und Entpacken ein. RAM-Bedarf
und Geschwindigkeit hängen vom Modell, der Audiositzung und dem PC ab;
garantierte Werte für jedes Gerät gibt es nicht.

## Lokale Daten

Audio und Transkripte bleiben auf dem PC; sie werden nicht an einen Spracherkennungsdienst hochgeladen. Bei Neuinstallationen ist der Verlauf standardmäßig deaktiviert. Einstellungen, Verlauf, Wörterbuch und Logs sind lokal und unverschlüsselt; vor dem Teilen prüfen. Logs sind auf eine aktive Datei mit 2 MiB und zwei Sicherungen begrenzt.

| Pfad | Inhalt |
|---|---|
| `%LOCALAPPDATA%\Murmur\settings.json` | Einstellungen |
| `%LOCALAPPDATA%\Murmur\history.jsonl` | Diktatverlauf |
| `%LOCALAPPDATA%\Murmur\dictionary.txt` | persönliches Wörterbuch |
| `%LOCALAPPDATA%\Murmur\murmur.log` | Anwendungsprotokoll |
| `%LOCALAPPDATA%\Murmur\models\` | heruntergeladene Sprachmodelle |
| `%LOCALAPPDATA%\Programs\Murmur` | Installation durch die Setup EXE |

## Funktionen

- Nimmt Mikrofon-Audio auf, solange die eingestellte Push-to-Talk-Taste gedrückt ist.
- Nutzt Parakeet v3 für fertige Transkripte nach dem Loslassen oder Nemotron 3.5 für Live-Textvorschauen im Aufnahmefenster. Nur ein Modell liegt gleichzeitig im Speicher.
- Bietet Standard- und Matheprofil. **F8** wechselt standardmäßig das Profil; Taste ändern oder Profil über Einstellungen und Tray-Menü wählen.
- Wandelt unterstützte gesprochene deutsche und englische Zahlen und Matheausdrücke im Matheprofil in Ziffern und mathematische Zeichen um. Als Ausgabe kannst du Unicode oder LaTeX wählen.
- Setzt Satzzeichen und entfernt auf Wunsch Füllwörter, getrennt für jedes Profil. Das persönliche Wörterbuch korrigiert Text vor dem Einfügen; Einträge lassen sich in den Einstellungen bearbeiten oder deaktivieren.
- Fügt Text am Cursor oder Dokumentende ein. Scheitert das Einfügen nach der Erkennung, bieten die Einstellungen den fertigen Text zum Kopieren an. Dieser Rettungstext ist vorübergehend und bleibt nach einem Neustart der App nicht erhalten.
- Speichert auf Wunsch einen Diktatverlauf. Einträge lassen sich kopieren und einzeln oder vollständig löschen. Wählbar sind 7, 30 oder 90 Tage Aufbewahrung oder keine Altersgrenze sowie maximal 1.000 Einträge oder keine Eintragsgrenze. Bei Neuinstallationen ist der Verlauf deaktiviert.
- Zeigt ein Aufnahmefenster, ohne dem Zielprogramm den Fokus zu nehmen. Seine Bildschirmposition lässt sich einstellen.
- Erlaubt Mikrofonwahl und Mikrofontest, englische oder deutsche Oberfläche sowie Autostart mit Windows.

Die Modellkarten von [Parakeet v3](https://huggingface.co/nvidia/parakeet-tdt-0.6b-v3) und [Nemotron 3.5](https://huggingface.co/nvidia/nemotron-3.5-asr-streaming-0.6b) nennen Deutsch und Englisch als unterstützte Sprachen. Die Erkennung bestimmt die gesprochene Sprache automatisch. Das Matheprofil akzeptiert beide Sprachen, unabhängig von der Oberflächensprache.

### Mathe diktieren

Das Matheprofil akzeptiert unterstützte deutsche und englische Zahlwörter und Matheausdrücke. Englische Zahlwörter werden für ganze Zahlen von 0 bis 999 erkannt; größere Zahlen kannst du als Ziffern diktieren. Unicode behält deutsche Dezimalkommas und englische Dezimalpunkte. LaTeX verwendet für beide einen Dezimalpunkt.

| Gesprochener Ausdruck (aktueller Quellstand) | Ausgabe im Unicode-Modus |
|---|---|
| `fünf mal drei gleich fünfzehn` | `5 × 3 = 15.` |
| `x gleich fünf komma null fünf` | `x = 5,05.` |
| `x hoch zwei` | `x².` |
| `wurzel aus x` | `√x.` |
| `five times three equals fifteen` | `5 × 3 = 15.` |
| `x equals five point zero five` | `x = 5.05.` |

Im LaTeX-Modus wird beispielsweise `s quadrat plus wurzel aus x` zu `s^2 + \sqrt{x}`. Mit **F8** wechselst du standardmäßig zwischen Standard- und Matheprofil. Die Formatierung nutzt feste Regeln; Murmur löst keine Gleichungen und schreibt Texte nicht mit einem Sprachmodell um.

Die Sprachmodelle laufen über sherpa-onnx. Murmur verwendet .NET 10 und Avalonia, die Audioaufnahme läuft über WASAPI mit NAudio.

### Optionales Audiofeedback

Für einen Ton nach dem Einfügen kannst du eine eigene WAV-Datei unter
`Sounds\feedback.wav` neben `Murmur.App.exe` ablegen. Aktiviere dazu Audiofeedback
in den Einstellungen. Murmur liefert keine Sounddateien mit und bleibt ohne
diese Datei stumm.

## Grenzen

Die Erkennungsgenauigkeit wurde nicht gemessen. Die Auswahl des Standardmodells beruht auf keinem Genauigkeitsvergleich.

Automatische Prüfungen decken nur einen Teil des Verhaltens ab. Die Kompatibilität mit jedem Mikrofon, PC und Zielprogramm wurde nicht geprüft. Windows verhindert das Einfügen in Anwendungen mit höheren Rechten als Murmur.

## Lizenz

Der Murmur-Quellcode steht unter der MIT-Lizenz. Hinweise zu Modellen und gebündelten Drittanbieterkomponenten stehen in [NOTICE.md](NOTICE.md).

## Build

```powershell
cd windows
dotnet restore Murmur.sln
dotnet build Murmur.sln --configuration Release --no-incremental -warnaserror
dotnet test Murmur.sln --configuration Release --no-build --filter 'Category!=RealModel&Category!=QualityCorpus'
```

Dieser Befehl führt die Unit- und UI-Tests der normalen CI aus. Der
Release-Workflow lädt zusätzlich Modelle herunter und führt Inferenztests der
Kategorie `RealModel` aus. `.github/workflows/real-model.yml` prüft Modelle
manuell oder nächtlich. Tests der Kategorie `QualityCorpus` benötigen einen
separat bereitgestellten Korpus.

Weitere Details: [Windows-Einrichtung und Build-Hinweise](windows/README.md).
