# Page spacing ownership

Desktop Shell.Content exclusively owns the outer page inset (20 horizontal, 16 vertical). Page controllers must not save, clear, or restore this shared padding. Settings and resources have no duplicate outer inset; card padding remains local. Navigation is expanded by default in product composition only. Compact settings retain control hit areas and natural wrapped description height.

Regression: consecutive Settings / Resources / Wardrobe / Home / Resource detail transitions retain the exact inset. Product rail starts with both expanded state and fully expanded presentation progress. Desktop 264 tests and native settings preview pass; compact/default/wide launch geometry is covered.
