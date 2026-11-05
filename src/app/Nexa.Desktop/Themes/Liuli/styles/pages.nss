/* Page-specific materials. Keep host geometry, imagery, status colors and input behavior. */
.launch-install-InstallJavaChoice, .launch-install-InstallBedrockChoice,
.launch-install-versions, .version-row, .version-directory-row, .version-choose,
.wardrobe-card, .wardrobe-library-card, .layout-WardrobePage-WardrobeCurrentCard,
.settings-content-card, .settings-content-detail, .settings-content-screenshot-card {
  Background: var(glass); Foreground: var(ink); Border: var(edge); BorderWidth: 1;
  CornerRadius: option(radius); Surface: Glass; Shadow: #244c6010 0 3 12;
}
.version-selected, .settings-content-card-selected, .wardrobe-choice-active,
.launch-install-choice-active, .launch-install-catalog-row-selected {
  Background: #c6e7e9b8; Border: #216f8260; Foreground: var(ink);
}
.launch-install-catalog-row { Foreground: var(ink); Hover: #c6e7e970; }
.launch-install-choice, .version-secondary, .version-input, .resource-button,
.wardrobe-button, .wardrobe-library-button, .wardrobe-card-apply,
.settings-content-button, .settings-content-pill, .feedback-dialog-secondary,
.command-palette-close, .layout-TaskCenterPage-TaskCenterClear, .layout-TaskCard-TaskCardRetry,
.layout-WardrobePage-WardrobeLibrary {
  Background: #e1eff2c0; Foreground: var(accent); Border: #ffffff70; BorderWidth: 1; Hover: #c9e7eb;
}
.launch-install-start, .layout-WardrobePage-WardrobeBrowse {
  Background: var(accent); Foreground: #ffffff; Hover: #195d70;
}
.resource-search-surface, .resource-extension, .settings-content-search,
.command-palette-search, .command-palette-row, .launch-install-input, .resource-filter-choice {
  Background: #ffffffa0; Foreground: var(ink); Border: #a4c2ca60; BorderWidth: 1; CornerRadius: 12;
}
.resource-search { Background: #00000000; BorderWidth: 0; }
.resource-filter-option { Background: #ffffff00; Foreground: var(ink); Hover: #c6e7e970; }
.resource-track, .java-install-navigation-surface,
.settings-content-category-track, .settings-content-binary-track {
  Background: #dcebefa8; Foreground: var(ink); CornerRadius: 12;
}
.resource-thumb, .launch-install-thumb, .settings-content-category-thumb {
  Background: #fffffff0; Foreground: var(accent); CornerRadius: 9; Shadow: #244c6018 0 2 5;
}
.resource-option, .resource-check, .launch-install-tab,
.settings-content-category, .settings-content-binary-option { Foreground: var(muted); Hover: #ffffff55; }
.launch-install-tab-active, .settings-content-category-selected,
.version-marker, .launch-install-catalog-check-selected, .settings-content-selection-mark { Foreground: var(accent); }
.settings-content-selection-mark { Background: var(accent); }

.version-title, .layout-WardrobePage-WardrobeCurrentTitle, .layout-WardrobePage-WardrobeIdentity,
.layout-WardrobePage-WardrobePageTitle, .layout-WardrobePage-WardrobeSkinTitle, .layout-WardrobePage-WardrobeCapeTitle,
.resource-project-title, .resource-detail-title, .resource-detail-versions-title, .resource-version-title,
.settings-content-title, .settings-content-hero-title, .settings-content-heading,
.settings-content-long, .settings-content-fact-value, .settings-content-toggle-label,
.settings-content-screenshot-card-name, .settings-content-screenshot-heading,
.wardrobe-card-title, .wardrobe-library-name,
.launch-install-InstallJavaTitle, .launch-install-InstallBedrockTitle,
.launch-install-catalog-name, .launch-install-catalog-title,
.layout-TaskCenterPage-TaskCenterTitle, .layout-TaskCenterPage-TaskCenterEmptyTitle,
.layout-TaskCard-TaskCardTitle, .layout-TaskCard-TaskCardPercent { Foreground: var(ink); }

.resource-project-description, .resource-project-metadata, .resource-message, .resource-status,
.resource-detail-author, .resource-detail-description, .resource-detail-notice, .resource-detail-info,
.resource-version-metadata, .resource-version-page-text, .resource-optional-summary,
.version-detail, .version-empty, .settings-content-path, .settings-content-count,
.settings-content-description, .settings-content-version, .settings-content-subtitle,
.settings-content-fact-label, .settings-content-empty, .settings-content-message,
.settings-content-screenshot-card-time, .wardrobe-card-source, .wardrobe-library-metadata,
.launch-install-catalog-detail, .launch-install-catalog-status,
.layout-TaskCenterPage-TaskCenterSummary, .layout-TaskCenterPage-TaskCenterEmptyHint,
.layout-TaskCard-TaskCardStage, .layout-TaskCard-TaskCardFiles, .layout-TaskCard-TaskCardSpeed,
.layout-TaskCard-TaskCardDetails, .layout-TaskCard-TaskCardAction,
.layout-WardrobePage-WardrobeCurrentSubtitle, .layout-WardrobePage-WardrobeProfileKind,
.layout-WardrobePage-WardrobeSkinSubtitle, .layout-WardrobePage-WardrobeCapeSubtitle,
.layout-WardrobePage-WardrobeSkinCount, .layout-WardrobePage-WardrobeCapeCount,
.layout-WardrobePage-WardrobeStatus, .layout-WardrobePage-WardrobeLocalStatus { Foreground: var(muted); }

/* Menus and modal sheets need a denser material than content cards. */
.layout-VersionDirectoryDropdown-LibraryDropdownCard, .feedback-dialog-card,
.command-palette-card, .layout-WardrobePage-WardrobeLocalPanel, .resource-filter-card {
  Background: #f7fbfcf5; Foreground: var(ink); Border: #ffffffd0; BorderWidth: 1;
  CornerRadius: option(radius); Surface: Solid; Shadow: #244c6030 0 8 24;
}
.feedback-dialog-title, .feedback-dialog-message, .feedback-notification-message,
.command-palette-title, .command-palette-empty { Foreground: var(ink); }
.feedback-dialog-scrim, .command-palette-scrim { Background: #162f4048; }
.feedback-notice-info { Background: #f0f8faf5; Border: #94c6d0; }
.layout-WardrobePage-WardrobeRailSurface { Background: #e6f0f360; CornerRadius: option(radius); }

@when mode = dark {
  .launch-install-InstallJavaChoice, .launch-install-InstallBedrockChoice,
  .launch-install-versions, .version-row, .version-directory-row, .version-choose,
  .wardrobe-card, .wardrobe-library-card, .layout-WardrobePage-WardrobeCurrentCard,
  .settings-content-card, .settings-content-detail, .settings-content-screenshot-card {
    Background: #192d3bce; Foreground: #e3f0f2; Border: #a7d9e022; Shadow: #050e1720 0 3 12;
  }
  .version-selected, .settings-content-card-selected, .wardrobe-choice-active,
  .launch-install-choice-active, .launch-install-catalog-row-selected {
    Background: #3b657aa0; Border: #a8e0e760; Foreground: #e3f0f2;
  }
  .launch-install-catalog-row { Foreground: #e3f0f2; Hover: #93d8df18; }
  .launch-install-choice, .version-secondary, .version-input, .resource-button,
  .wardrobe-button, .wardrobe-library-button, .wardrobe-card-apply,
  .settings-content-button, .settings-content-pill, .feedback-dialog-secondary,
  .command-palette-close, .layout-TaskCenterPage-TaskCenterClear, .layout-TaskCard-TaskCardRetry,
  .layout-WardrobePage-WardrobeLibrary {
    Background: #6eacbc20; Foreground: #b6e4e9; Border: #b0dfe32a; Hover: #80c7d33b;
  }
  .resource-search-surface, .resource-extension, .settings-content-search,
  .command-palette-search, .command-palette-row, .launch-install-input, .resource-filter-choice {
    Background: #102431a8; Foreground: #e3f0f2; Border: #8fbacb30;
  }
  .resource-filter-option { Foreground: #e3f0f2; Hover: #93d8df18; }
  .resource-track, .java-install-navigation-surface,
  .settings-content-category-track, .settings-content-binary-track { Background: #071c2a65; Foreground: #e3f0f2; }
  .resource-thumb, .launch-install-thumb, .settings-content-category-thumb { Background: #3b5e70d0; Foreground: #b6e4e9; }
  .resource-option, .resource-check, .launch-install-tab,
  .settings-content-category, .settings-content-binary-option { Foreground: #a4bdc7; Hover: #a0dce318; }
  .launch-install-tab-active, .settings-content-category-selected,
  .version-marker, .launch-install-catalog-check-selected { Foreground: #c3eef2; }
  .settings-content-selection-mark { Background: #a8e0e7; Foreground: #a8e0e7; }
  .version-title, .layout-WardrobePage-WardrobeCurrentTitle, .layout-WardrobePage-WardrobeIdentity,
  .layout-WardrobePage-WardrobePageTitle, .layout-WardrobePage-WardrobeSkinTitle, .layout-WardrobePage-WardrobeCapeTitle,
  .resource-project-title, .resource-detail-title, .resource-detail-versions-title, .resource-version-title,
  .settings-content-title, .settings-content-hero-title, .settings-content-heading,
  .settings-content-long, .settings-content-fact-value, .settings-content-toggle-label,
  .settings-content-screenshot-card-name, .settings-content-screenshot-heading,
  .wardrobe-card-title, .wardrobe-library-name,
  .launch-install-InstallJavaTitle, .launch-install-InstallBedrockTitle,
  .launch-install-catalog-name, .launch-install-catalog-title,
  .layout-TaskCenterPage-TaskCenterTitle, .layout-TaskCenterPage-TaskCenterEmptyTitle,
  .layout-TaskCard-TaskCardTitle, .layout-TaskCard-TaskCardPercent { Foreground: #e3f0f2; }
  .resource-project-description, .resource-project-metadata, .resource-message, .resource-status,
  .resource-detail-author, .resource-detail-description, .resource-detail-notice, .resource-detail-info,
  .resource-version-metadata, .resource-version-page-text, .resource-optional-summary,
  .version-detail, .version-empty, .settings-content-path, .settings-content-count,
  .settings-content-description, .settings-content-version, .settings-content-subtitle,
  .settings-content-fact-label, .settings-content-empty, .settings-content-message,
  .settings-content-screenshot-card-time, .wardrobe-card-source, .wardrobe-library-metadata,
  .launch-install-catalog-detail, .launch-install-catalog-status,
  .layout-TaskCenterPage-TaskCenterSummary, .layout-TaskCenterPage-TaskCenterEmptyHint,
  .layout-TaskCard-TaskCardStage, .layout-TaskCard-TaskCardFiles, .layout-TaskCard-TaskCardSpeed,
  .layout-TaskCard-TaskCardDetails, .layout-TaskCard-TaskCardAction,
  .layout-WardrobePage-WardrobeCurrentSubtitle, .layout-WardrobePage-WardrobeProfileKind,
  .layout-WardrobePage-WardrobeSkinSubtitle, .layout-WardrobePage-WardrobeCapeSubtitle,
  .layout-WardrobePage-WardrobeSkinCount, .layout-WardrobePage-WardrobeCapeCount,
  .layout-WardrobePage-WardrobeStatus, .layout-WardrobePage-WardrobeLocalStatus { Foreground: #a4bdc7; }
  .layout-VersionDirectoryDropdown-LibraryDropdownCard, .feedback-dialog-card,
  .command-palette-card, .layout-WardrobePage-WardrobeLocalPanel, .resource-filter-card {
    Background: #172c3af5; Foreground: #e3f0f2; Border: #a7d9e044; Shadow: #050e1750 0 8 24;
  }
  .feedback-dialog-title, .feedback-dialog-message, .feedback-notification-message,
  .command-palette-title, .command-palette-empty { Foreground: #e3f0f2; }
  .feedback-dialog-scrim, .command-palette-scrim { Background: #050e1780; }
  .feedback-notice-info { Background: #172c3af5; Border: #699da9; }
  .feedback-notice-warn { Background: #322c20f5; }
  .feedback-notice-error { Background: #34242bf5; }
  .layout-WardrobePage-WardrobeRailSurface { Background: #14243170; }
}
