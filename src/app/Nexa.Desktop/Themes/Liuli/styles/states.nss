/* Secondary views and transient states use the same materials as the stable pages. */
.theme-surface { Background: #00000000; BorderWidth: 0; Surface: None; Shadow: #00000000 0 0 0; }
.theme-entry {
  Background: #ffffff70; Foreground: var(ink); Border: #ffffff90; BorderWidth: 1;
  CornerRadius: 12; Surface: Solid; Margin: 0,0,0,8;
}
.theme-step, .launch-memory-clean, .java-choice-button, .official-download-secondary {
  Background: #e1eff2c0; Foreground: var(accent); Border: #ffffff90; BorderWidth: 1; Hover: #c9e7eb;
}
.launch-memory-clean { CornerRadius: 14; }
.theme-watch, .theme-option-value, .theme-file-path,
.launch-memory-title, .launch-memory-usage, .launch-memory-toggle, .launch-memory-deep,
.launch-memory-threshold-label { Foreground: var(ink); }
.theme-path, .launch-memory-detail { Foreground: var(muted); }
.theme-selected { Foreground: var(accent); }
.theme-slider, .launch-memory-threshold { Foreground: var(accent); Background: #8aafbd60; }
.theme-switch { Foreground: var(accent); }
.theme-choices { Background: #dcebefa8; Foreground: var(ink); }
.theme-selector-thumb { Background: #fffffff0; Foreground: var(accent); }
.theme-choice { Foreground: var(accent); Hover: #ffffff55; }
/* Checkbox text is not its checked fill. Supply a separate border/accent so a
   light text color cannot become a white box with a white check mark. */
CheckBox { Border: #93adba; BorderWidth: 1; }
CheckBox:checked { Border: var(accent); }
.loading-hint, .launch-install-catalog-status { Foreground: var(ink); }
/* Legacy hosts use a disabled retry button for status copy. Avoid adding another
   theme dimming layer; current hosts use an ordinary, noninteractive Text node. */
.launch-install-catalog-status:disabled {
  Opacity: 1; Background: #00000000; BorderWidth: 0; Surface: None; Foreground: var(ink); FontWeight: 600;
}
ProgressRing { Foreground: var(accent); }
.launch-install-home-profile, .official-download-card {
  Background: var(glass); Foreground: var(ink); Border: var(edge); BorderWidth: 1; CornerRadius: option(radius); Surface: Glass;
}
.launch-install-home-profile-selected, .wardrobe-library-selected { Background: #c6e7e9b8; Foreground: var(accent); }
.official-download-notes, .resource-project-icon, .resource-detail-icon,
.settings-content-icon, .settings-content-hero-icon { Background: #e4f1f580; }
.settings-content-screenshot-dialog { Background: #f7fbfcf5; Foreground: var(ink); Border: #ffffffd0; BorderWidth: 1; }
.settings-content-screenshot-input { Background: #ffffffa0; Foreground: var(ink); Border: #a4c2ca60; BorderWidth: 1; }
.settings-content-graph-canvas { Background: #f7fbfc; Foreground: var(ink); }
.layout-LaunchingPage-LaunchProgressTrack { Background: #8aafbd60; }

@when mode = dark {
  .theme-entry { Background: #21384780; Foreground: #e3f0f2; Border: #a7d9e02e; }
  .theme-step, .launch-memory-clean, .java-choice-button, .official-download-secondary { Background: #6eacbc20; Foreground: #b6e4e9; Border: #b0dfe32a; Hover: #80c7d33b; }
  .theme-watch, .theme-option-value, .theme-file-path,
  .launch-memory-title, .launch-memory-usage, .launch-memory-toggle, .launch-memory-deep,
  .launch-memory-threshold-label { Foreground: #e3f0f2; }
  .theme-path, .launch-memory-detail { Foreground: #c4d8e2; }
  .theme-selected { Foreground: #a8e0e7; }
  .theme-slider, .launch-memory-threshold { Foreground: #9de2ed; Background: #96abbc60; }
  .theme-choices { Background: #071c2a65; Foreground: #e3f0f2; }
  .theme-selector-thumb { Background: #3b5e70d0; Foreground: #b6e4e9; }
  .theme-choice { Foreground: #c3eef2; Hover: #a0dce318; }
  /* Loading/empty messages can sit directly on the wallpaper, without a card. */
  .loading-hint, .launch-install-catalog-status, .launch-install-catalog-status:disabled,
  .resource-message, .settings-content-empty, .version-empty { Foreground: #ffffff; }
  ProgressRing { Foreground: #9de2ed; }
  .launch-install-home-profile, .official-download-card { Background: #192d3bce; Foreground: #e3f0f2; Border: #a7d9e022; }
  .launch-install-home-profile-selected, .wardrobe-library-selected { Background: #3b657aa0; Foreground: #c3eef2; }
  .official-download-notes, .resource-project-icon, .resource-detail-icon,
  .settings-content-icon, .settings-content-hero-icon { Background: #42647730; }
  .official-download-java { Background: #24536b; Foreground: #a8e0e7; }
  .official-download-bedrock { Background: #2f5147; Foreground: #b7e2bd; }
  .official-download-java-ink { Foreground: #a8e0e7; }
  .official-download-bedrock-ink { Foreground: #b7e2bd; }
  .settings-content-screenshot-dialog { Background: #172c3af5; Foreground: #e3f0f2; Border: #a7d9e044; }
  .settings-content-screenshot-input { Background: #102431a8; Foreground: #e3f0f2; Border: #8fbacb30; }
  .settings-content-graph-canvas { Background: #122733; Foreground: #e3f0f2; }
  .layout-LaunchingPage-LaunchProgressTrack { Background: #96abbc60; }
}
