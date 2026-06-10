#!/usr/bin/env node
// Lucide.cs 생성 스크립트 (일회성, 출력 파일을 커밋한다)
//
// 사용법:
//   1. npm pack lucide-static && tar xzf lucide-static-*.tgz  (아이콘 SVG 확보)
//   2. node tools/generate-lucide.mjs <path-to-lucide-package/icons>
//
// Material 아이콘 이름 → Lucide 아이콘 매핑. 마이그레이션 시
// Icons.Material.*.{Key} 를 Lucide.{PascalCase(value)} 로 치환한다.
// 출력: src/Seoro.Shared/UiKit/Lucide.cs, tools/material-to-lucide.md

import { readFileSync, writeFileSync, readdirSync } from 'node:fs';
import { join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';

const MAPPING = {
  AccessTime: 'clock',
  AccountBalance: 'landmark',
  AccountBalanceWallet: 'wallet',
  AccountCircle: 'circle-user',
  AccountTree: 'git-fork',
  Add: 'plus',
  AddCircleOutline: 'circle-plus',
  AllInclusive: 'infinity',
  Analytics: 'chart-column',
  ArrowBack: 'arrow-left',
  ArrowDropDown: 'chevron-down',
  ArrowForward: 'arrow-right',
  ArrowUpward: 'arrow-up',
  AttachFile: 'paperclip',
  AttachMoney: 'dollar-sign',
  AutoAwesome: 'sparkles',
  AutoFixHigh: 'wand-sparkles',
  Bedtime: 'moon',
  Block: 'ban',
  Bolt: 'zap',
  Brush: 'brush',
  Build: 'wrench',
  Cake: 'cake',
  CalendarMonth: 'calendar-days',
  CalendarToday: 'calendar',
  CallMerge: 'git-merge',
  CallSplit: 'git-branch',
  Cancel: 'circle-x',
  Chat: 'message-circle',
  ChatBubble: 'message-square',
  ChatBubbleOutline: 'message-square',
  Check: 'check',
  CheckBox: 'square-check',
  CheckBoxOutlineBlank: 'square',
  CheckCircle: 'circle-check',
  Checklist: 'list-checks',
  ChevronLeft: 'chevron-left',
  ChevronRight: 'chevron-right',
  ClearAll: 'list-x',
  Close: 'x',
  Cloud: 'cloud',
  CloudDownload: 'cloud-download',
  CloudUpload: 'cloud-upload',
  Code: 'code',
  Collections: 'images',
  Construction: 'construction',
  ContentCopy: 'copy',
  Contrast: 'contrast',
  CreateNewFolder: 'folder-plus',
  CurrencyExchange: 'circle-dollar-sign',
  DarkMode: 'moon',
  Dashboard: 'layout-dashboard',
  DataObject: 'braces',
  DataUsage: 'chart-pie',
  Delete: 'trash-2',
  DeleteOutline: 'trash',
  DeleteSweep: 'trash-2',
  Description: 'file-text',
  DesktopWindows: 'monitor',
  DeviceHub: 'network',
  Diamond: 'gem',
  Difference: 'diff',
  DirectionsRun: 'footprints',
  Diversity3: 'users',
  Dns: 'server',
  Download: 'download',
  DriveFileRenameOutline: 'file-pen',
  Edit: 'pencil',
  EditNote: 'square-pen',
  ElectricBolt: 'zap',
  Email: 'mail',
  EmojiEvents: 'trophy',
  Engineering: 'hard-hat',
  Error: 'circle-alert',
  ErrorOutline: 'circle-alert',
  EventAvailable: 'calendar-check',
  ExpandLess: 'chevron-up',
  ExpandMore: 'chevron-down',
  Explore: 'compass',
  Extension: 'puzzle',
  Factory: 'factory',
  Favorite: 'heart',
  FileDownload: 'file-down',
  FilterList: 'list-filter',
  FilterListOff: 'filter-x',
  FilterNone: 'layers-2',
  FindInPage: 'file-search',
  FitnessCenter: 'dumbbell',
  FlashOn: 'zap',
  Folder: 'folder',
  FolderOff: 'folder-x',
  FolderOpen: 'folder-open',
  FolderSpecial: 'folder-dot',
  Forum: 'messages-square',
  GeneratingTokens: 'coins',
  GridView: 'layout-grid',
  Handyman: 'hammer',
  Hardware: 'hammer',
  HelpOutline: 'circle-help',
  History: 'history',
  HourglassEmpty: 'hourglass',
  HourglassTop: 'hourglass',
  Http: 'globe',
  Hub: 'network',
  Image: 'image',
  Info: 'info',
  InsertDriveFile: 'file',
  Insights: 'chart-line',
  Inventory: 'package',
  Key: 'key',
  Keyboard: 'keyboard',
  KeyboardArrowDown: 'chevron-down',
  Label: 'tag',
  Language: 'globe',
  LastPage: 'arrow-right-to-line',
  Layers: 'layers',
  LightMode: 'sun',
  Link: 'link',
  LocalFireDepartment: 'flame',
  Lock: 'lock',
  LunchDining: 'sandwich',
  ManageAccounts: 'user-cog',
  ManageSearch: 'text-search',
  Map: 'map',
  MarkChatRead: 'check-check',
  Memory: 'cpu',
  MilitaryTech: 'medal',
  ModeNight: 'moon',
  MonetizationOn: 'circle-dollar-sign',
  MoreHoriz: 'ellipsis',
  MoreVert: 'ellipsis-vertical',
  NetworkCheck: 'activity',
  NewReleases: 'badge-alert',
  Nightlight: 'moon',
  NoteAdd: 'file-plus',
  Notes: 'notebook-text',
  Notifications: 'bell',
  NotificationsActive: 'bell-ring',
  NotificationsNone: 'bell',
  OpenInNew: 'external-link',
  Output: 'arrow-right-from-line',
  Paid: 'circle-dollar-sign',
  Pause: 'pause',
  PauseCircle: 'circle-pause',
  Payments: 'credit-card',
  Person: 'user',
  PersonAdd: 'user-plus',
  PlayArrow: 'play',
  PlayCircle: 'circle-play',
  PlaylistAdd: 'list-plus',
  PrecisionManufacturing: 'cog',
  Psychology: 'brain',
  Public: 'globe',
  QuestionAnswer: 'messages-square',
  RadioButtonUnchecked: 'circle',
  RateReview: 'message-square-text',
  Refresh: 'refresh-cw',
  Remove: 'minus',
  RestartAlt: 'rotate-ccw',
  RocketLaunch: 'rocket',
  Rule: 'list-todo',
  Save: 'save',
  Savings: 'piggy-bank',
  Schedule: 'clock',
  School: 'graduation-cap',
  Search: 'search',
  Security: 'shield',
  Send: 'send',
  Settings: 'settings',
  Shield: 'shield',
  SmartToy: 'bot',
  Sms: 'message-square-text',
  Source: 'folder-code',
  Speed: 'gauge',
  Star: 'star',
  Stop: 'square',
  StopCircle: 'circle-stop',
  Storage: 'database',
  Store: 'store',
  SwitchAccount: 'users-round',
  Sync: 'refresh-cw',
  SyncDisabled: 'refresh-cw-off',
  SystemUpdate: 'hard-drive-download',
  SystemUpdateAlt: 'hard-drive-download',
  Tab: 'app-window',
  Terminal: 'terminal',
  ThumbUp: 'thumbs-up',
  Timer: 'timer',
  ToggleOff: 'toggle-left',
  ToggleOn: 'toggle-right',
  Token: 'coins',
  TravelExplore: 'telescope',
  TrendingUp: 'trending-up',
  Tune: 'sliders-horizontal',
  Undo: 'undo-2',
  UnfoldLess: 'chevrons-down-up',
  UnfoldMore: 'chevrons-up-down',
  Verified: 'badge-check',
  VideoLibrary: 'film',
  ViewColumn: 'columns-3',
  ViewSidebar: 'panel-left',
  ViewStream: 'rows-3',
  Visibility: 'eye',
  Warning: 'triangle-alert',
  WavingHand: 'hand',
  WbSunny: 'sun',
  WbTwilight: 'sunset',
  Webhook: 'webhook',
  Weekend: 'sofa',
  Whatshot: 'flame',
  WifiOff: 'wifi-off',
  WorkspacePremium: 'award',
  Workspaces: 'boxes',
};

// 마이그레이션 외 UI 킷 자체에서 쓰는 추가 아이콘
const EXTRA = [
  'chevrons-left', 'chevrons-right', 'circle-x', 'eye-off', 'maximize-2',
  'minimize-2', 'panel-right', 'pin', 'pin-off', 'plus', 'loader-circle',
  'arrow-down', 'arrow-up-right', 'corner-down-left', 'grip-vertical',
];

const iconsDir = process.argv[2];
if (!iconsDir) {
  console.error('Usage: node tools/generate-lucide.mjs <lucide-package/icons>');
  process.exit(1);
}

const pascal = (kebab) => kebab.split('-').map(s => /^\d/.test(s) ? s : s[0].toUpperCase() + s.slice(1)).join('');

const needed = [...new Set([...Object.values(MAPPING), ...EXTRA])].sort();
const missing = needed.filter(n => {
  try { readFileSync(join(iconsDir, n + '.svg')); return false; } catch { return true; }
});
if (missing.length) {
  console.error('Missing lucide icons: ' + missing.join(', '));
  process.exit(1);
}

const inner = (name) => {
  const svg = readFileSync(join(iconsDir, name + '.svg'), 'utf8');
  const m = svg.match(/<svg[^>]*>([\s\S]*)<\/svg>/);
  return m[1].replace(/\s+/g, ' ').replace(/> </g, '><').trim();
};

const repoRoot = join(dirname(fileURLToPath(import.meta.url)), '..');

let cs = `// <auto-generated>
// Lucide 아이콘 (https://lucide.dev) — lucide-static에서 추출, ISC License
// Copyright (c) Lucide Contributors 2022.
// 재생성: tools/generate-lucide.mjs 참고. 직접 수정 금지.
// </auto-generated>

namespace Seoro.Shared.UiKit;

/// <summary>
/// Lucide 아이콘의 24x24 stroke SVG 내부 마크업 상수.
/// &lt;Icon Svg="@Lucide.X" /&gt; 형태로 사용한다.
/// </summary>
public static class Lucide
{
`;
for (const name of needed) {
  cs += `    public const string ${pascal(name)} = """${inner(name)}""";\n`;
}
cs += `}\n`;

writeFileSync(join(repoRoot, 'src/Seoro.Shared/UiKit/Lucide.cs'), cs);

let md = `# Material → Lucide 아이콘 매핑\n\n마이그레이션 시 \`Icons.Material.{Variant}.{Material}\` → \`Lucide.{Lucide}\` 로 치환.\n(Material Filled/Outlined 변형은 동일 Lucide 아이콘으로 통합)\n\n| Material | Lucide 상수 |\n|---|---|\n`;
for (const [mat, luc] of Object.entries(MAPPING).sort((a, b) => a[0].localeCompare(b[0]))) {
  md += `| ${mat} | Lucide.${pascal(luc)} |\n`;
}
writeFileSync(join(repoRoot, 'tools/material-to-lucide.md'), md);

console.log(`Generated ${needed.length} icons → src/Seoro.Shared/UiKit/Lucide.cs`);
console.log(`Mapping table → tools/material-to-lucide.md (${Object.keys(MAPPING).length} entries)`);
