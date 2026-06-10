# MudBlazor → Seoro UI Kit 마이그레이션 가이드 (에이전트용)

새 컴포넌트: `src/Seoro.Shared/Components/Ui/` (네임스페이스 자동 import됨 — `_Imports.razor`에 등록 완료).
Enum: `Seoro.Shared.UiKit` (`Tone`, `UiSize`, `ButtonStyle`, `ToastSeverity`, `PopoverPlacement`, `UiPlacement`).
아이콘: `Lucide.X` 상수 (`src/Seoro.Shared/UiKit/Lucide.cs`), Material→Lucide 매핑: `tools/material-to-lucide.md`.
CSS: `src/Seoro.Desktop/wwwroot/css/ui.css` (`.ui-*`), 디자인 토큰: `tokens.css`.

## 디자인 방침
- **컴팩트/미니멀**: 새 컴포넌트 기본 크기가 Mud보다 작다 (버튼 26px, 아이콘 16px). 그대로 두면 자연스럽게 밀도가 높아진다.
- 불필요한 래퍼 div, 과한 padding/margin은 정리. 기존 기능·동작은 100% 유지.

## 컴포넌트 변환표

### MudIcon → Icon
`<MudIcon Icon="@Icons.Material.Filled.X" Size="Size.Small" Class=".." Style=".."/>`
→ `<Icon Svg="@Lucide.매핑값" Size="14" Class=".." Style=".."/>`
- Size: `Size.Small`→14, 기본(Medium)→16~18, `Size.Large`→20~24. Size 파라미터는 int(px).
- Color="Color.X" → `Tone="Tone.X"` (Success/Warning/Error/Info/Primary/Secondary). Color.Default→생략.
- 매핑 예: CheckCircle→CircleCheck, Add→Plus, Close→X, Delete→Trash2, Edit→Pencil, ExpandMore→ChevronDown, ExpandLess→ChevronUp, Warning→TriangleAlert, Error→CircleAlert, Folder→Folder, Search→Search, Refresh→RefreshCw, Terminal→Terminal, Psychology→Brain, AutoAwesome→Sparkles, SmartToy→Bot, AccountTree→GitFork, CallMerge→GitMerge, CallSplit→GitBranch, ContentCopy→Copy, Description→FileText, InsertDriveFile→File. **반드시 tools/material-to-lucide.md에서 확인** — Lucide.cs에 없는 상수를 만들어내지 말 것.

### MudButton → Button
`<MudButton Variant="Variant.Filled" Color="Color.Primary" Size="Size.Small" StartIcon="@Icons..." OnClick=".." Disabled="..">텍스트</MudButton>`
→ `<Button ButtonStyle="ButtonStyle.Filled" Tone="Tone.Primary" StartIcon="@Lucide.X" OnClick=".." Disabled="..">텍스트</Button>`
- Variant.Filled→Filled, Variant.Outlined→Outlined, Variant.Text→Text. 지정 없음→Subtle(기본).
- Size.Small→생략(기본 Sm), Size.Medium→`Size="UiSize.Md"`.
- FullWidth, EndIcon, Title, Class, Style 동일. `Style="text-transform: none;"` 등 Mud 보정 스타일은 삭제 (기본이 미니멀).

### MudIconButton → IconButton
`<MudIconButton Icon="@Icons..." Size="Size.Small" Color="Color.X" OnClick=".." Title=".."/>`
→ `<IconButton Icon="@Lucide.X" Tone="Tone.X" OnClick=".." Title=".."/>`
- Icon 파라미터는 Lucide SVG 상수 string. Size.Small→생략. Disabled/Class/Style 동일. `Active`(bool) 파라미터로 토글 상태 표현 가능.
- MudToggleIconButton → IconButton + `Active="@상태"` + OnClick에서 토글.

### MudTextField → TextField
`<MudTextField @bind-Value="x" Label=".." Placeholder=".." HelperText=".." Immediate="true" Lines="3" Variant=".." Margin=".." Dense.. />`
→ `<TextField @bind-Value="x" Label=".." Placeholder=".." HelperText=".." Immediate="true" Lines="3"/>`
- Variant/Margin/Dense류 제거. `T="string"` 제거 (string 전용).
- InputType.Password → `Type="password"`. OnKeyDown/OnBlur/AutoFocus/Disabled/ReadOnly/ErrorText/Error/AdornmentIcon(+AdornmentEnd) 지원. `@ref` 후 `FocusAsync()` 가능.
- 값이 string이 아닌 MudTextField(T="int" 등)는 NumericField<T>로.

### MudNumericField → NumericField / MudSlider → Slider
동일 파라미터: @bind-Value, Min, Max, Step, Label. T는 struct INumber<T>.

### MudSelect / MudSelectItem → Select<T> / SelectItem<T>
`<MudSelect T="X" @bind-Value=".." Label=".."><MudSelectItem Value="..">표시</MudSelectItem></MudSelect>`
→ `<Select T="X" @bind-Value=".." Label=".."><SelectItem T="X" Value=".." Text="트리거표시텍스트">표시콘텐츠</SelectItem></Select>`
- 커스텀 표시(아이템 내부 마크업)는 ChildContent로 유지하되, **트리거에 보일 텍스트는 `Text` 파라미터나 Select의 `ToStringFunc`로 지정** (지정 없으면 Value.ToString()).
- 스크롤 컨테이너 내부면 `Fixed="true"`.
- AnchorOrigin/TransformOrigin/Variant/Dense 제거.

### MudMenu / MudMenuItem → Menu / MenuItem
`<MudMenu Icon="@Icons..." AnchorOrigin=".." ...><MudMenuItem OnClick=".." Icon="..">라벨</MudMenuItem></MudMenu>`
→ `<Menu Icon="@Lucide.EllipsisVertical" Placement="PopoverPlacement.BottomEnd"><MenuItem OnClick=".." Icon="@Lucide.X">라벨</MenuItem></Menu>`
- ActivatorContent 지원: `<Menu><ActivatorContent>커스텀트리거</ActivatorContent><ChildContent><MenuItem../></ChildContent></Menu>`
- 파괴적 항목: `Tone="Tone.Error"`. Dense="true" 지원.
- **스크롤 컨테이너(목록/사이드바/탭바) 내부의 메뉴는 `Fixed="true"` 필수** (클리핑 방지).
- AnchorOrigin/TransformOrigin → Placement (BottomStart/BottomEnd/TopStart/TopEnd 중 가장 가까운 것).

### MudPopover (+MudOverlay 닫기 패턴) → Popover
기존 패턴:
```razor
<MudOverlay Visible="_open" OnClick="() => _open=false" .../>
<MudPopover Open="_open" AnchorOrigin=".." TransformOrigin="..">패널내용</MudPopover>
```
→
```razor
<Popover @bind-Open="_open" Placement="PopoverPlacement.BottomStart">
    <AnchorContent>트리거버튼(클릭 시 _open 토글)</AnchorContent>
    <ChildContent>패널내용</ChildContent>
</Popover>
```
- 백드롭 클릭 닫기/Escape 내장. MudOverlay 줄 삭제. 트리거 요소는 AnchorContent 안으로 이동(앵커 기준 위치 잡힘).
- RelativeWidth/Paper류 파라미터 제거. 패널 스타일은 ChildContent 안에서.

### MudTooltip → Tooltip
`<MudTooltip Text="..">대상</MudTooltip>` → `<Tooltip Text="..">대상</Tooltip>`
- Placement="UiPlacement.Top|Bottom|Left|Right" (기본 Top). RootClass/Arrow류 제거. TooltipContent(RenderFragment) 지원.

### MudProgressCircular → Spinner
`<MudProgressCircular Indeterminate="true" Size="Size.Small" Color="Color.Primary"/>` → `<Spinner Size="16"/>`
- Size: Small→14~16, Medium→20, Large→28. Tone 기본 Primary.

### MudProgressLinear → ProgressBar
`<MudProgressLinear Value="x" Color="..(Indeterminate)"/>` → `<ProgressBar Value="x" Tone=".." Indeterminate="true"/>`

### MudAlert → Alert
`<MudAlert Severity="Severity.Warning" Dense="true">내용</MudAlert>` → `<Alert Tone="Tone.Warning" Dense="true">내용</Alert>`
- Severity.Normal→Tone.Default, Info→Info 등. NoIcon, OnClose 지원.

### MudSwitch → Switch
`<MudSwitch T="bool" @bind-Value="x" Label=".." Color="Color.Primary"/>` → `<Switch @bind-Value="x" Label=".."/>`

### MudChip / MudChipSet → Chip
`<MudChip T="string" Size="Size.Small" Color="Color.X" Variant=".." Icon=".." OnClose=".." OnClick="..">라벨</MudChip>`
→ `<Chip Tone="Tone.X" Icon="@Lucide.X" OnClose=".." OnClick="..">라벨</Chip>`
- T= 제거. Size.Small→생략. Variant.Outlined→`ButtonStyle="ButtonStyle.Outlined"`. Selected(bool) 지원.
- MudChipSet → 일반 `<div class="d-flex flex-wrap gap-1">` + Chip들 (선택 로직은 OnClick에서 직접).

### 기타 단순 변환
- MudDivider → `<Divider/>`, 수직: `<Divider Vertical="true"/>`
- MudText Typo="Typo.h6|subtitle1|subtitle2|body2|caption|overline" → 일반 태그 + class `typo-h6|typo-subtitle1|typo-subtitle2|typo-body2|typo-caption|typo-overline` (tokens.css). Color="Color.Secondary"→class `text-secondary`.
- MudPaper → `<div class="ui-card">`
- MudLink → `<Link Href=".." Target="..">`
- MudAvatar → `<Avatar Size="28">`, MudBadge → `<Badge Content=".." Dot Tone Visible>`
- MudSimpleTable → `<table class="ui-table">`
- MudList/MudListItem → 일반 마크업 (div/button + 필요시 .ui-menu__item 스타일 참고)
- MudOverlay 단독(스피너 백드롭 등) → `<div class="ui-modal-backdrop">` 또는 기존 overlay.css 클래스 활용
- MudClickAwayListener → Popover 패턴으로 대체하거나 케이스별 판단

### 다이얼로그 (IDialogService / MudDialog)
- `@inject IDialogService DialogService` → `@inject IModalService ModalService` (`Seoro.Shared.Services.Ui`, import됨)
- `await DialogService.ShowMessageBoxAsync(title, msg, yesText, cancelText: c, noText: n)` → `await ModalService.ShowMessageBoxAsync(title, msg, yesText, noText: n, cancelText: c)` — **반환 bool? 3상태(yes=true/no=false/취소·dismiss=null) 동일**
- `await DialogService.ShowAsync<TDialog>("제목", parameters, options)` → `ModalService.Show<TDialog>("제목", new ModalParameters { ["ParamName"] = value })`; 결과: `var result = await ref.Result;` → `ModalResult` (`Canceled`, `Data`, `As<T>()`)
- MudDialog 컴포넌트 변환: `[CascadingParameter] IMudDialogInstance MudDialog` → `[CascadingParameter] IModalInstance Modal`; `MudDialog.Close(DialogResult.Ok(x))` → `Modal.Close(ModalResult.Ok(x))`; `MudDialog.Cancel()` → `Modal.Cancel()`
- `<MudDialog><DialogContent>...</DialogContent><DialogActions>...</DialogActions></MudDialog>` → 래퍼 제거, 본문 마크업 + 하단 `<div class="ui-modal__footer">버튼들</div>` (패널/제목은 ModalHost가 렌더)
- IsOpen 바인딩형 다이얼로그(MudDialog @bind-Visible 또는 자체 오버레이) → 선언형 `<Modal @bind-IsOpen Title MaxWidth Footer>` 사용 가능

### Toast
- 이미 전환됨: `@inject IToastService Snackbar` + `Snackbar.Add(msg, ToastSeverity.X)`. 새 코드도 동일 패턴 사용.

### CSS 변수 (--mud-palette-*) — 마이그레이션하는 파일에서 발견 시 치환
- primary→`--accent`, primary-darken→`--accent-hover`, primary-lighten→`--accent-hover`, primary-text→`--text-inverse`
- `rgba(var(--mud-palette-primary-rgb), 0.1)` 류 → `--accent-dim`(0.10) / `--accent-subtle`(0.06)
- success/warning/error/info→`--color-success|warning|error|info` (+`-bg`, `-border` 변형 활용), tertiary→`--color-success`
- text-primary→`--text-primary`, text-secondary→`--text-secondary`, text-disabled→`--text-muted`
- lines-default→`--border-default`, action-default-hover→`--bg-hover`, action-disabled-background→`--bg-active`
- surface→`--bg-surface`, background→`--bg-base`, background-grey→`--bg-sidebar`
- success-darken/lighten 등 파생 → 가장 가까운 토큰 또는 `--color-*` 본색

## 규칙
1. 기능/동작/바인딩/이벤트 흐름을 바꾸지 말 것 — 시각 컴포넌트만 교체.
2. `@using MudBlazor`는 _Imports에 있으므로 파일별 제거 불필요. 파일 내 Mud 참조를 0으로 만드는 것이 목표.
3. Lucide.cs에 없는 아이콘 상수 금지 — 매핑표에 없으면 의미상 가장 가까운 기존 상수 선택.
4. 한 파일 안에서 Mud를 전부 제거하지 못할 애매한 케이스(복잡한 동작 의존)는 건너뛰고 보고서에 명시.
5. dotnet build는 실행하지 말 것 (오케스트레이터가 일괄 빌드).
