/* Original NexaCL glass treatment. No scripts, behavior overrides or layout shifts. */
@tokens {
  accent: option(accent);
  glass: #ffffffb8;
  edge: #ffffffd0;
  ink: #253e49;
  muted: #5b7580;
  tint: #c6e7e970;
}

Shell { Background: #eef5f8b8; }
ContentHost.layout-Shell-14 { Background: #00000000; Surface: None; }
TitleBar { Background: #e5f1f5d8; Foreground: var(ink); BorderWidth: 0; Surface: Glass; }
Text#TitleMain, Text#TitleSubpage { Foreground: var(ink); FontWeight: 600; }
Button#TitleBack { Foreground: var(ink); Background: #ffffff00; Hover: #ffffff70; }
Navigation { Background: #e6f0f3b8; Foreground: var(muted); Surface: Glass; }
NavigationItem { Foreground: var(muted); CornerRadius: 12; Hover: var(tint); Margin: 6,2,6,2; }
NavigationItem:selected { Foreground: var(accent); Background: #ffffffa8; }

#CardAccount, #CardVersion, #CardAbout,
.world-card, .theme-surface, .resource-card, .resource-project,
.platform-card, .settings-form-surface, .settings-diagnostics-card, .settings-custom-update, .settings-disk-log-card,
.layout-TaskCard-TaskCard, .layout-VersionSelectionRow-LibraryRow {
  Background: var(glass); Foreground: var(ink); Border: var(edge); BorderWidth: 1;
  CornerRadius: option(radius); Surface: Glass;
  Shadow: #244c6012 0 6 20;
}
#CardAccount { BackgroundGradient: linear-gradient(135deg,#ffffff88 0%,#dbf2f240 100%); }
#AccountName, #AccountHeader, #VersionName, #AboutMessage, #TriviaMessage, #EchoMessage,
.settings-catalog-label, .theme-name, .theme-option-label, .world-message,
.world-server-name, .world-online-name, .settings-category-title { Foreground: var(ink); }
#AccountKind, #AccountHint, #VersionHeader, #AboutTitle, #TriviaTitle, #EchoTitle,
.theme-version, .theme-description, .theme-group, .settings-save-status, .world-counter { Foreground: var(muted); }

Button { CornerRadius: 12; }
.capsule, #AccountAdd, #AccountBack, #AccountImport, #AccountSwitch, #AccountWardrobe, #InstanceListButton, #InstanceSettings, #InstanceModify,
.theme-action, .theme-action-small, .settings-catalog-action {
  Background: #e1eff2c0; Foreground: var(accent); Border: #ffffff90; BorderWidth: 1; Hover: #c9e7eb;
}
Button:hover { Border: var(accent); }
Button:focus-visible, TextInput:focus-visible { Border: var(accent); BorderWidth: 2; }
#AccountAdd:focus-visible, #AccountBack:focus-visible, #AccountImport:focus-visible,
#AccountSwitch:focus-visible, #AccountWardrobe:focus-visible, #InstanceListButton:focus-visible,
#InstanceSettings:focus-visible, #InstanceModify:focus-visible, #LaunchButton:focus-visible, #TitleBack:focus-visible {
  Border: var(accent); BorderWidth: 2;
}
Button:disabled { Opacity: 0.5; }
#LaunchButton, #LaunchButtonProgress, .feedback-dialog-primary, .official-download-primary, .version-primary {
  Background: var(accent); Foreground: #ffffff; BorderWidth: 0; CornerRadius: 22; Hover: #195d70;
}
#LaunchButtonText { Foreground: #ffffff; }
#AccountAvatarSurface, #InstanceRow { Background: #e4f1f580; Foreground: var(ink); Border: #ffffff70; BorderWidth: 1; }
#WidgetAboutDot, #WidgetTriviaDot, #WidgetEchoDot { Background: var(accent); }

TextInput, .settings-catalog-input, .settings-editor-input, .theme-input, .world-input {
  Background: #ffffffa0; Foreground: var(ink); Border: #a4c2ca60; BorderWidth: 1; CornerRadius: 10;
}
.settings-navigation-surface, .settings-catalog-selector, .settings-catalog-mode {
  Background: #dcebefa8; Foreground: var(ink); CornerRadius: 12;
}
.settings-navigation-surface { CornerRadius: 16; Border: #ffffff70; BorderWidth: 1; }
.settings-navigation-thumb, .settings-catalog-thumb {
  Background: #fffffff0; Foreground: var(accent); CornerRadius: 10; Shadow: #244c6018 0 2 5;
}
.settings-navigation-option, .settings-catalog-option { Foreground: var(muted); Hover: #ffffff55; }
.settings-navigation-option-selected, .settings-catalog-option-selected { Foreground: var(accent); }
.settings-catalog-divider, .theme-option-divider { Background: #8aafbd25; }

@when mode = dark {
  Shell { Background: #101b26ce; }
  TitleBar { Background: #152534d9; Foreground: #e3f0f2; }
  Text#TitleMain, Text#TitleSubpage, Button#TitleBack { Foreground: #e3f0f2; }
  Navigation { Background: #142431c8; Foreground: #a4bdc7; }
  NavigationItem { Foreground: #a4bdc7; Hover: #93d8df18; }
  NavigationItem:selected { Foreground: #a8e0e7; Background: #93d8df20; }
  #CardAccount, #CardVersion, #CardAbout,
  .world-card, .theme-surface, .resource-card, .resource-project,
  .platform-card, .settings-form-surface, .settings-diagnostics-card, .settings-custom-update, .settings-disk-log-card,
  .layout-TaskCard-TaskCard, .layout-VersionSelectionRow-LibraryRow {
    Background: #192d3bce; Foreground: #e3f0f2; Border: #a7d9e022; Shadow: #050e1730 0 6 20;
  }
  #CardAccount { BackgroundGradient: linear-gradient(135deg,#29495850 0%,#1d304330 100%); }
  #AccountName, #AccountHeader, #VersionName, #AboutMessage, #TriviaMessage, #EchoMessage,
  .settings-catalog-label, .theme-name, .theme-option-label, .world-message,
  .world-server-name, .world-online-name, .settings-category-title { Foreground: #e3f0f2; }
  #AccountKind, #AccountHint, #VersionHeader, #AboutTitle, #TriviaTitle, #EchoTitle,
  .theme-version, .theme-description, .theme-group, .settings-save-status, .world-counter { Foreground: #a4bdc7; }
  .capsule, #AccountAdd, #AccountBack, #AccountImport, #AccountSwitch, #AccountWardrobe, #InstanceListButton, #InstanceSettings, #InstanceModify,
  .theme-action, .theme-action-small, .settings-catalog-action {
    Background: #6eacbc20; Foreground: #b6e4e9; Border: #b0dfe32a; Hover: #80c7d33b;
  }
  #AccountAvatarSurface, #InstanceRow { Background: #42647730; Foreground: #e3f0f2; Border: #b0dfe318; }
  TextInput, .settings-catalog-input, .settings-editor-input, .theme-input, .world-input {
    Background: #102431a8; Foreground: #e3f0f2; Border: #8fbacb30;
  }
  .settings-navigation-surface, .settings-catalog-selector, .settings-catalog-mode { Background: #071c2a65; Foreground: #e3f0f2; }
  .settings-navigation-thumb, .settings-catalog-thumb { Background: #3b5e70d0; Foreground: #b6e4e9; }
  .settings-navigation-option, .settings-catalog-option { Foreground: #a4bdc7; Hover: #a0dce318; }
  .settings-navigation-option-selected, .settings-catalog-option-selected { Foreground: #c3eef2; }
  .settings-catalog-divider, .theme-option-divider { Background: #a7d9e018; }
  Button:focus-visible, TextInput:focus-visible,
  #AccountAdd:focus-visible, #AccountBack:focus-visible, #AccountImport:focus-visible,
  #AccountSwitch:focus-visible, #AccountWardrobe:focus-visible, #InstanceListButton:focus-visible,
  #InstanceSettings:focus-visible, #InstanceModify:focus-visible, #LaunchButton:focus-visible, #TitleBack:focus-visible { Border: #a8e0e7; }
}
