/* Auxiliary flows keep their compact tool layout, with the same material hierarchy. */
.setup-surface, .settings-content-file-card, .settings-launcher-migration-instance,
.process-log-output, .layout-LaunchingPage-LaunchingCard {
  Background: var(glass); Foreground: var(ink); Border: var(edge); BorderWidth: 1;
  CornerRadius: option(radius); Surface: Glass;
}
.account-input, .settings-custom-input, .settings-proxy-input, .settings-workspace-input,
.instance-tools-editor, .instance-tools-line-number, .process-log-search-surface {
  Background: #ffffffa0; Foreground: var(ink); Border: #a4c2ca60; BorderWidth: 1; CornerRadius: 10;
}
.account-button, .setup-button, .setup-consent-off, .process-log-button,
.settings-proxy-action, .settings-network-refresh, .settings-network-probe,
.settings-java-refresh, .settings-diagnostics-refresh, .platform-refresh, .settings-recovery-button,
.settings-custom-update-check, .layout-LaunchingPage-LaunchingCancelButton,
.layout-LaunchingPage-LaunchingHintBox {
  Background: #e1eff2c0; Foreground: var(accent); Hover: #c9e7eb;
}
.account-primary, .setup-primary, .instance-tools-area-selected {
  Background: var(accent); Foreground: #ffffff; Hover: #195d70;
}
.process-log-source-track, .settings-proxy-track, .settings-java-enabled-track {
  Background: #dcebefa8; Foreground: var(ink); CornerRadius: 10;
}
.process-log-source-thumb, .settings-proxy-thumb, .instance-launch-tab-selected {
  Background: #fffffff0; Foreground: var(accent); CornerRadius: 8;
}
.setup-consent-on, .process-log-row-selected { Background: #c6e7e9b8; Foreground: var(ink); }
.setup-title, .account-code, .process-log-session, .process-log-row { Foreground: var(ink); }
.setup-description, .setup-progress, .account-help, .process-log-status, .process-log-page-number,
.process-log-empty, .layout-LaunchingPage-LaunchingHintTitle, .layout-LaunchingPage-LaunchingHintValue {
  Foreground: var(muted);
}
.process-log-row { Hover: #c6e7e940; }
.layout-LaunchingPage-LaunchProgressFill { Background: var(accent); Foreground: var(accent); }
.platform-badge { Background: #dcebefa8; Foreground: var(muted); }

@when mode = dark {
  .setup-surface, .settings-content-file-card, .settings-launcher-migration-instance,
  .process-log-output, .layout-LaunchingPage-LaunchingCard { Background: #192d3bce; Foreground: #e3f0f2; Border: #a7d9e022; }
  .account-input, .settings-custom-input, .settings-proxy-input, .settings-workspace-input,
  .instance-tools-editor, .instance-tools-line-number, .process-log-search-surface {
    Background: #102431a8; Foreground: #e3f0f2; Border: #8fbacb30;
  }
  .account-button, .setup-button, .setup-consent-off, .process-log-button,
  .settings-proxy-action, .settings-network-refresh, .settings-network-probe,
  .settings-java-refresh, .settings-diagnostics-refresh, .platform-refresh, .settings-recovery-button,
  .settings-custom-update-check, .layout-LaunchingPage-LaunchingCancelButton,
  .layout-LaunchingPage-LaunchingHintBox { Background: #6eacbc20; Foreground: #b6e4e9; Hover: #80c7d33b; }
  .process-log-source-track, .settings-proxy-track, .settings-java-enabled-track { Background: #071c2a65; Foreground: #e3f0f2; }
  .process-log-source-thumb, .settings-proxy-thumb, .instance-launch-tab-selected { Background: #3b5e70d0; Foreground: #b6e4e9; }
  .setup-consent-on, .process-log-row-selected { Background: #3b657aa0; Foreground: #e3f0f2; }
  .setup-title, .account-code, .process-log-session, .process-log-row { Foreground: #e3f0f2; }
  .setup-description, .setup-progress, .account-help, .process-log-status, .process-log-page-number,
  .process-log-empty, .layout-LaunchingPage-LaunchingHintTitle, .layout-LaunchingPage-LaunchingHintValue { Foreground: #a4bdc7; }
  .process-log-row { Hover: #93d8df18; }
  .platform-badge { Background: #071c2a65; Foreground: #a4bdc7; }
  .account-primary, .setup-primary, .instance-tools-area-selected { Background: var(accent); Foreground: #ffffff; Hover: #195d70; }
}
