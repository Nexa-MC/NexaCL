@tokens {
  accent: option(accent);
  card-radius: option(radius);
}

Text#TitleMain {
  Foreground: #ffffff;
  FontSize: option(title-size);
  FontFamily: asset("fonts/DejaVuSans.ttf");
  Transform: translate(1,0) scale(1) rotate(0);
}

Button.capsule:hover {
  Border: var(accent);
  BorderWidth: 1;
  Shadow: #10204030 0 2 6;
}

StackPanel#CardAccount {
  CornerRadius: var(card-radius);
  Shadow: #10204028 0 3 10;
  BackgroundGradient: linear-gradient(45deg,#eef4ff 0%,#e2eaff 100%);
  GridColumn: 0;
  GridRow: 0;
}
StackPanel#RightColumn { GridColumn: 1; GridRow: 0; }

@supports grid {
  StackPanel#LaunchHomeWorkspace {
    GridColumns: 1fr 1fr;
    GridRows: 1fr;
    ColumnGap: 24;
    RowGap: 16;
  }
  @viewport width <= 900 {
    StackPanel#LaunchHomeWorkspace { GridColumns: 1fr; GridRows: 1fr 1fr; }
    StackPanel#CardAccount { MaxWidth: 100000; }
    StackPanel#RightColumn { GridColumn: 0; GridRow: 1; }
  }
}

StackPanel#VersionActions > Button#InstanceSettings { Order: 0; }
StackPanel#VersionActions > Button#InstanceModify { Order: 1; }

Progress#LaunchButtonProgress {
  Position: Overlay;
  Left: 0;
  Top: 0;
  Right: 0;
  Bottom: 0;
}

@when mode = dark {
  StackPanel#CardAccount { Background: #172138; Border: #34496b; BackgroundGradient: linear-gradient(45deg,#172138 0%,#243057 100%); }
}
@when platform = linux {
  Button.capsule:focus-visible { Border: #68a5ff; BorderWidth: 2; }
}

@keyframes title-breathe {
  0% { Opacity: 0.82; TranslateY: 0; }
  100% { Opacity: 1; TranslateY: 1; }
}
@when option(ornament) = true {
  Text#TitleMain { Animation: title-breathe 2s infinite alternate; }
}
