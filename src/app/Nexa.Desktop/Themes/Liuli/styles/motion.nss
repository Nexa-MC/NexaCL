/* Original finite motion tracks. Curves are sampled into native keyframes;
   layout and pointer routing remain host-owned. Reduced motion is enforced by Host. */
@keyframes liuli-card {
  0% { Opacity: 0; TranslateY: 8; }
  18% { Opacity: 0.55; TranslateY: 3.8; }
  42% { Opacity: 0.92; TranslateY: 0.9; }
  70% { Opacity: 1; TranslateY: 0; }
  100% { Opacity: 1; TranslateY: 0; }
}
@keyframes liuli-card-second {
  0% { Opacity: 0; TranslateY: 8; }
  12% { Opacity: 0; TranslateY: 8; }
  30% { Opacity: 0.65; TranslateY: 3; }
  58% { Opacity: 0.96; TranslateY: 0.4; }
  82% { Opacity: 1; TranslateY: 0; }
  100% { Opacity: 1; TranslateY: 0; }
}
@keyframes liuli-card-third {
  0% { Opacity: 0; TranslateY: 8; }
  22% { Opacity: 0; TranslateY: 8; }
  40% { Opacity: 0.65; TranslateY: 3; }
  68% { Opacity: 0.96; TranslateY: 0.4; }
  88% { Opacity: 1; TranslateY: 0; }
  100% { Opacity: 1; TranslateY: 0; }
}
@when option(motion) = true {
  #CardAccount { Animation: liuli-card 260ms; }
  #CardVersion { Animation: liuli-card-second 300ms; }
  #CardAbout { Animation: liuli-card-third 340ms; }
  .launch-install-InstallJavaChoice { Animation: liuli-card 260ms; }
  .launch-install-InstallBedrockChoice { Animation: liuli-card-second 300ms; }
  Button:focus-visible, TextInput:focus-visible { Shadow: #216f8230 0 0 8; }
  /* Host owns interruptible hover/press, anchored dialog enter/exit, leaf stagger,
     segmented springs, capsule expansion and inertia. Never layer keyframes on
     those interactions or on recycled/scrolling rows: it would restart on retarget. */
}
