# Design Document: Whisper V3 Turbo, Sprach-Selbstkorrektur & Zielfenster-Wiederherstellung

**Datum:** 2026-10-12  
**Status:** Approved  
**Autor:** Antigravity & Murmur Team  

---

## 1. Zielsetzung & Überblick

Dieses Dokument beschreibt die Erweiterung von **Murmur** um drei Kernfunktionen:
1. **Whisper Large-V3-Turbo als Speech-to-Text Engine:** Hochpräzise, rein lokale Offline-Transkription (Push-to-Talk) über das bereits in Murmur integrierte `sherpa-onnx`-Framework.
2. **Automatische Selbstkorrektur bei Versprechern (Speech Repair):** Herausfiltern von Versprechern, Gedankensprüngen und Korrekturfloskeln (*„Ach ja, wir treffen uns Dienstag. Ach nee, Mittwoch“* $\rightarrow$ *„Wir treffen uns Mittwoch“*) durch ein eingebettetes, extrem leichtgewichtiges lokales Sprachmodell (Qwen 2.5 0.5B Instruct GGUF via `LLamaSharp`).
3. **Erhalten des Zielfensters bei Fokuswechsel (Target Window Preservation):** Wenn der Nutzer während des Drückens der Push-to-Talk-Taste in ein anderes Fenster wechselt, bringt Murmur vor dem Einfügen automatisch das ursprüngliche Fenster wieder in den Vordergrund, tippt den Text ein und kann optional zum Zwischenfenster zurückkehren.

---

## 2. Architektur & Komponenten

```
[ Mikrofon-Audio (WASAPI) ]
           │
           ▼
[ SherpaOnnxWhisperTranscriber ] ──> Rohtext (z. B. "Dienstag. Ach nee, Mittwoch.")
           │
           ▼
[ SlmSpeechRepairer (Qwen 2.5) ] ──> Bereinigter Text ("Mittwoch.")
           │
           ▼
[ RuleBasedFormatter ]           ──> Interpunktion, Satzanfang-Großschreibung
           │
           ▼
[ DictionaryCorrector ]          ──> Persönliches Wörterbuch
           │
           ▼
[ SendInputInjector ]            ──> Prüft Fokus, stellt Zielfenster via Win32 wieder her,
                                     tippt Text per SendInput ein
```

---

## 3. Komponente 1: Whisper Large-V3-Turbo (`Murmur.Speech`)

### 3.1 Modell-Dateien & Bereitstellung
Whisper V3 Turbo wird über vorkonvertierte, int8-quantisierte ONNX-Modelle (`csukuangfj/sherpa-onnx-whisper-turbo`) bereitgestellt:
* `turbo-encoder.int8.onnx` (~675 MB)
* `turbo-decoder.int8.onnx` (~361 MB)
* `tokens.txt` (~817 KB)
* Speicherort: `%LOCALAPPDATA%\Murmur\models\whisper-v3-turbo\`

### 3.2 Manifest-Erweiterung (`ModelManifest.cs`)
* Neuer Eintrag `WhisperV3Turbo`:
  * `Id`: `"whisper-v3-turbo"`
  * `DisplayName`: `"Whisper Large-V3-Turbo (offline)"`
  * `SupportsLivePreview`: `false` (rein offline / Push-to-Talk)
  * `RequiredFiles`: `["turbo-encoder.int8.onnx", "turbo-decoder.int8.onnx", "tokens.txt"]`

### 3.3 Transkriber-Implementierung (`SherpaOnnxWhisperTranscriber.cs`)
* Implementiert `ITranscriber`.
* Konfiguration des `OfflineRecognizerConfig`:
  * `FeatConfig.FeatureDim = 128` (Whisper benötigt 128 Mel-Bins)
  * `FeatConfig.SampleRate = 16000`
  * `ModelConfig.Whisper.Encoder = Path.Combine(dir, "turbo-encoder.int8.onnx")`
  * `ModelConfig.Whisper.Decoder = Path.Combine(dir, "turbo-decoder.int8.onnx")`
  * `ModelConfig.Whisper.Language = ""` (automatische Spracherkennung für DE / EN)
  * `ModelConfig.Whisper.Task = "transcribe"`
  * `ModelConfig.Tokens = Path.Combine(dir, "tokens.txt")`
  * `ModelConfig.NumThreads = 4`
  * `ModelConfig.Provider = "cpu"`

---

## 4. Komponente 2: Sprach-Selbstkorrektur (`Murmur.Core` & `Murmur.Speech`)

### 4.1 Modell & Inferenz (LLamaSharp)
* Paket: `LLamaSharp` und `LLamaSharp.Backend.Cpu`.
* Modell: **Qwen 2.5 0.5B Instruct (Q4_K_M GGUF, ~397 MB)**.
* Speicherort: `%LOCALAPPDATA%\Murmur\models\qwen-2.5-0.5b\`.
* Latenz auf moderner CPU: ca. 60–120 ms für typische Diktatsätze.

### 4.2 Schnittstelle & Repairer
```csharp
public interface ISpeechRepairer
{
    Task<string> RepairAsync(string rawTranscript, CancellationToken cancellationToken = default);
}
```

* **System-Prompt:**
  ```text
  Du bist ein präziser Editor für gesprochene Diktate. Deine Aufgabe ist es, Versprecher, Selbstkorrekturen (z. B. 'ach ja...', 'ach nee...', 'oder besser...', 'ich meinte...') und Füllwörter zu entfernen. Behalte den Satzbau und den restlichen Wortlaut exakt bei. Gib ausschließlich das korrigierte Diktat aus, ohne Einleitung oder Anmerkungen.
  ```
* **Generierungs-Parameter:**
  * `Temperature`: 0.0 (deterministisch, reproduzierbar, keine Halluzinationen)
  * `TopP`: 1.0
  * `MaxTokens`: Wortanzahl des Rohtranskripts + 32 Tokens
* **Fallback-Verhalten:**
  * Sollte das SLM-Modell deaktiviert, nicht heruntergeladen oder überlastet sein, wird das Rohtranskript unverändert an `RuleBasedFormatter` weitergereicht. Kein Diktat geht verloren.

---

## 5. Komponente 3: Zielfenster-Wiederherstellung (`Murmur.Platform.Windows`)

### 5.1 Fokus-Handling in `SendInputInjector.cs`
Bisher wurde bei `!IsSameTarget(target, currentTarget)` die Exception `TargetChanged` geworfen.
Neu:
1. Ermitteln des aktuellen Vordergrundfensters `currentTarget`.
2. Wenn `!IsSameTarget(target, currentTarget)`:
   * Speichere `intermediateWindow = currentTarget.WindowHandle`.
   * Prüfe `Win32.IsWindow(target.WindowHandle)`. Wenn ungültig $\rightarrow$ Fehler `TargetUnavailable`.
   * Falls minimiert (`IsIconic`): `ShowWindow(target.WindowHandle, SW_RESTORE)`.
   * Reaktivierung via Thread-Kopplung:
     ```csharp
     var curThread = Win32.GetCurrentThreadId();
     var foreThread = Win32.GetWindowThreadProcessId(Win32.GetForegroundWindow(), out _);
     var targetThread = Win32.GetWindowThreadProcessId(target.WindowHandle, out _);

     Win32.AttachThreadInput(curThread, foreThread, true);
     Win32.AttachThreadInput(curThread, targetThread, true);
     Win32.SetForegroundWindow(target.WindowHandle);
     Win32.BringWindowToTop(target.WindowHandle);
     Win32.AttachThreadInput(curThread, foreThread, false);
     Win32.AttachThreadInput(curThread, targetThread, false);
     ```
   * Warte bis zu 100 ms, bis `GetForegroundWindow() == target.WindowHandle`.
3. Ausführen von `SendInput`.
4. Wenn `RestorePreviousWindowAfterInjection == true` und `intermediateWindow != 0`:
   * Reaktivierung von `intermediateWindow` nach dem Tippen.

### 5.2 Einstellungen (`AppSettings.cs`)
* Neue Optionen:
  * `EnableSelfCorrection` (bool, Standard: `true`)
  * `RestorePreviousWindowAfterInjection` (bool, Standard: `false`)
* Integration in die Einstellungsansicht (`SettingsWindow.axaml`) mit deutscher und englischer Lokalisierung.

---

## 6. Fehlerbehandlung & Sicherheit

1. **Fenster wurde geschlossen:** Text wird in Zwischenablage / Historie gerettet.
2. **Modifizierer-Tasten (Strg, Alt, Shift):** Werden weiterhin vor `SendInput` abgefangen, um unerwünschte Hotkeys zu verhindern.
3. **Modell-Downloads:** Integritätsprüfung über SHA-256 Hashes vor der ersten Verwendung.

---

## 7. Test-Strategie

1. **Unit-Tests in `Murmur.Speech.Tests`:**
   * Konfigurationstest für Whisper Transcriber (FeatureDim = 128, File Paths).
2. **Unit-Tests in `Murmur.Core.Tests`:**
   * Testen der Reparaturlogik mit Mocks für typische Versprecher-Phrasen (*„Dienstag ... ach nee, Mittwoch“*).
   * Verifikation des Fallback-Verhaltens bei deaktiviertem SLM.
3. **Unit-Tests in `Murmur.Platform.Windows.Tests`:**
   * Testen der Target-Matching- und Fokus-Wiederherstellungslogik in `SendInputInjector`.
