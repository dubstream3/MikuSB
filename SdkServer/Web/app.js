'use strict';

// ---------------------------------------------------------------------------------------------
// MikuSB admin panel — vanilla ES2020, no build step, no dependencies.
// Page chrome is localised in this file; activity / character / skin names are localised by the
// server from Resources/all_language (see ReplayCatalog.Locales for the supported set).
// ---------------------------------------------------------------------------------------------

const TOKEN_KEY = 'mikush.admin.token';
const LANG_KEY = 'mikush.admin.lang';

const LANGS = [
  { id: 'zh_CN', label: '简体中文' },
  { id: 'zh_TW', label: '繁體中文' },
  { id: 'ja_JP', label: '日本語' },
  { id: 'ko_KR', label: '한국어' },
  { id: 'en_US', label: 'English' },
  { id: 'fr_FR', label: 'Français' },
  { id: 'es_ES', label: 'Español' },
  { id: 'de_DE', label: 'Deutsch' },
  { id: 'th_TH', label: 'ไทย' },
  { id: 'id_ID', label: 'Bahasa Indonesia' }
];

const STRINGS = {
  zh_CN: {
    'app.title': 'MikuSB 管理面板',
    'brand.sub': '私服管理',
    'nav.overview': '总览',
    'nav.accounts': '账号',
    'nav.online': '在线',
    'nav.replay': '活动复刻',
    'nav.console': '控制台',
    'status.connecting': '连接中…',
    'status.offline': '未连接',
    'status.running': 'v{0} · 运行 {1}',
    'status.error': '接口错误',
    'login.title': '需要访问令牌',
    'login.hint': '令牌保存在服务端 <code>Config/admin-token.txt</code>（也在 <code>Config.json</code> 的 <code>HttpServer.AdminToken</code>）；启动时它会自动复制到剪贴板，并用带令牌的链接打开浏览器。',
    'login.placeholder': '粘贴令牌',
    'login.submit': '进入',
    'login.invalid': '令牌无效或已变更，请重新输入。',
    'login.connect': '无法连接服务端：{0}',
    'load.failed': '加载失败',
    'dur.hm': '{0} 小时 {1} 分',
    'dur.ms': '{0} 分 {1} 秒',
    'dur.s': '{0} 秒',
    'overview.title': '总览',
    'overview.accounts': '账号',
    'overview.online': '在线玩家',
    'overview.commands': '控制台指令',
    'overview.uptime': '运行时长',
    'overview.server': '服务器',
    'overview.name': '名称',
    'overview.version': '构建版本',
    'overview.database': '数据库',
    'overview.loaded': '已加载',
    'overview.loading': '加载中',
    'overview.note': '面板是本机回环端口上的纯 HTTP 服务；账号增删、货币发放等写操作都经服务端命令层执行。',
    'accounts.title': '账号',
    'accounts.total': '共 {0} 个',
    'accounts.search': '搜索 UID / 用户名',
    'accounts.searchBtn': '搜索',
    'accounts.newUsername': '新账号用户名',
    'accounts.newUid': 'UID（可空）',
    'accounts.newPassword': '密码（可空）',
    'accounts.create': '创建',
    'accounts.th.uid': 'UID',
    'accounts.th.user': '用户名',
    'accounts.th.status': '状态',
    'accounts.th.ban': '封禁',
    'accounts.th.perms': '权限',
    'accounts.online': '在线',
    'accounts.offline': '离线',
    'accounts.normal': '正常',
    'accounts.empty': '没有匹配的账号',
    'accounts.prev': '上一页',
    'accounts.next': '下一页',
    'accounts.page': '第 {0} 页 · 共 {1} 页',
    'accounts.detail': '详情',
    'accounts.delete': '删除',
    'accounts.confirmDelete': '删除账号 {0}（UID {1}）？该账号的玩家数据会一并删除。',
    'accounts.created': '已创建 {0}（UID {1}）',
    'accounts.deleted': '已删除 UID {0}',
    'accounts.nameRequired': '用户名不能为空',
    'online.title': '在线',
    'online.count': '{0} 人在线',
    'online.refresh': '刷新',
    'online.th.name': '角色名',
    'online.th.level': '等级',
    'online.th.remote': '来源地址',
    'online.empty': '当前没有玩家在线',
    'online.unnamed': '（未命名）',
    'player.charName': '角色名',
    'player.level': '等级',
    'player.vigor': '体力',
    'player.items': '背包物品',
    'player.weapons': '武器',
    'player.skins': '皮肤',
    'player.currencies': '货币（在线时可直接改）',
    'player.query': '查询',
    'player.add': '增加',
    'player.set': '设为',
    'player.offlineHint': '玩家离线：货币指令要求目标在线',
    'player.th.id': 'ID',
    'player.th.name': '名称',
    'player.th.balance': '余额',
    'player.noCurrency': '该账号还没有货币数据',
    'player.details': '详情',
    'player.ban': '封禁',
    'player.registered': '注册时间',
    'player.lastActive': '最后活跃',
    'player.none': '该账号还没有游戏数据',
    'replay.title': '活动复刻',
    'replay.available': '{0} 期可选',
    'replay.current': '当前服务期号',
    'replay.none': '未固定',
    'replay.mode': '生效方式',
    'replay.mode.runtime': '运行期切换',
    'replay.mode.persisted': '配置文件固定',
    'replay.mode.client': '跟随客户端',
    'replay.confirm': '确定复刻',
    'replay.selected': '已选：{0}',
    'replay.pick': '先点一张期号卡，再按「确定复刻」。',
    'replay.applied': '已切换到第 {0} 期。',
    'replay.baseOutfit': '基础外观',
    'notice.title': '请先读这条提醒',
    'notice.footer': '本服务器完全免费。如果你为它付过钱，请按上面的说明退款并举报卖家。',
    'notice.ok': '我知道了',
    'replay.retired': '数据已删除',
    'replay.retired.tip': '第 {0} 期的数据已从资源包中删除，无法复刻。',
    'replay.same': '该期已在服务中。',
    'replay.mainUi': '主界面',
    'replay.window': '时间窗',
    'replay.filter': '筛选期号 / 名称 / 角色',
    'replay.save': '写入 Config.json（重启后保留）',
    'replay.off': '取消复刻',
    'replay.hint': '点卡片即切换该期；勾选则同时写进配置文件，不勾只影响本次运行。',
    'replay.inUse': '在用',
    'replay.era': '第 {0} 期',
    'replay.castCount': '{0} 角色 · {1} 套皮肤',
    'replay.cast': '涉及角色与皮肤',
    'replay.skinTitle': '皮肤',
    'replay.noMatch': '没有匹配的期号',
    'replay.select': '选一张卡片查看该期涉及的角色与皮肤',
    'replay.noCast': '该期没有记录到角色 / 皮肤（TryID 为空）',
    'replay.noActivity': '该期没有关联活动',
    'replay.switched': '已切换到第 {0} 期',
    'replay.stopped': '已取消复刻',
    'replay.missing': '资源库里找不到 <code>dlc/dlc_activities.json</code>（当前根目录：<code>{0}</code>）。',
    'replay.longTerm': '长期',
    'console.title': '控制台',
    'console.quick': '快捷命令，如 /money list @1',
    'console.run': '执行',
    'console.input': '一行一条：/money add 3 10000 @1',
    'console.clear': '清空输出',
    'console.identity': '命令以服务端控制台身份执行，拥有全部权限。',
    'console.commands': '可用指令（{0}）',
    'console.th.cmd': '指令',
    'console.th.usage': '用法',
    'console.th.desc': '说明',
    'console.noOutput': '（无输出）',
    'common.refresh': '刷新',
    'common.copy': '复制'
  },
  en_US: {
    'app.title': 'MikuSB admin panel',
    'brand.sub': 'Server admin',
    'nav.overview': 'Overview',
    'nav.accounts': 'Accounts',
    'nav.online': 'Online',
    'nav.replay': 'Era replay',
    'nav.console': 'Console',
    'status.connecting': 'Connecting…',
    'status.offline': 'Offline',
    'status.running': 'v{0} · up {1}',
    'status.error': 'API error',
    'login.title': 'Access token required',
    'login.hint': 'The token is stored in <code>Config/admin-token.txt</code> on the server (and in <code>HttpServer.AdminToken</code> of <code>Config.json</code>). On start it is copied to the clipboard and the browser opens with the token already applied.',
    'login.placeholder': 'Paste token',
    'login.submit': 'Enter',
    'login.invalid': 'Token is invalid or has changed. Try again.',
    'login.connect': 'Cannot reach the server: {0}',
    'load.failed': 'Failed to load',
    'dur.hm': '{0}h {1}m',
    'dur.ms': '{0}m {1}s',
    'dur.s': '{0}s',
    'overview.title': 'Overview',
    'overview.accounts': 'Accounts',
    'overview.online': 'Players online',
    'overview.commands': 'Console commands',
    'overview.uptime': 'Uptime',
    'overview.server': 'Server',
    'overview.name': 'Name',
    'overview.version': 'Build',
    'overview.database': 'Database',
    'overview.loaded': 'loaded',
    'overview.loading': 'loading',
    'overview.note': 'The panel is plain HTTP on a loopback port; account edits and currency grants all run through the server command layer.',
    'accounts.title': 'Accounts',
    'accounts.total': '{0} total',
    'accounts.search': 'Search UID / username',
    'accounts.searchBtn': 'Search',
    'accounts.newUsername': 'Username',
    'accounts.newUid': 'UID (optional)',
    'accounts.newPassword': 'Password (optional)',
    'accounts.create': 'Create',
    'accounts.th.uid': 'UID',
    'accounts.th.user': 'Username',
    'accounts.th.status': 'Status',
    'accounts.th.ban': 'Ban',
    'accounts.th.perms': 'Permissions',
    'accounts.online': 'online',
    'accounts.offline': 'offline',
    'accounts.normal': 'normal',
    'accounts.empty': 'No account matches',
    'accounts.prev': 'Prev',
    'accounts.next': 'Next',
    'accounts.page': 'Page {0} of {1}',
    'accounts.detail': 'Details',
    'accounts.delete': 'Delete',
    'accounts.confirmDelete': 'Delete account {0} (UID {1})? Its player data is removed as well.',
    'accounts.created': 'Created {0} (UID {1})',
    'accounts.deleted': 'Deleted UID {0}',
    'accounts.nameRequired': 'Username is required',
    'online.title': 'Online',
    'online.count': '{0} online',
    'online.refresh': 'Refresh',
    'online.th.name': 'Character',
    'online.th.level': 'Level',
    'online.th.remote': 'Remote',
    'online.empty': 'No player is online',
    'online.unnamed': '(unnamed)',
    'player.charName': 'Character',
    'player.level': 'Level',
    'player.vigor': 'Vigor',
    'player.items': 'Items',
    'player.weapons': 'Weapons',
    'player.skins': 'Skins',
    'player.currencies': 'Currency (editable while online)',
    'player.query': 'Get',
    'player.add': 'Add',
    'player.set': 'Set',
    'player.offlineHint': 'Player is offline: the money commands need a live session',
    'player.th.id': 'ID',
    'player.th.name': 'Name',
    'player.th.balance': 'Balance',
    'player.noCurrency': 'No currency rows yet',
    'player.details': 'Details',
    'player.ban': 'Ban',
    'player.registered': 'Registered',
    'player.lastActive': 'Last active',
    'player.none': 'No game data for this account yet',
    'replay.title': 'Era replay',
    'replay.available': '{0} eras available',
    'replay.current': 'Serving now',
    'replay.none': 'not pinned',
    'replay.mode': 'Mode',
    'replay.mode.runtime': 'runtime pin',
    'replay.mode.persisted': 'pinned in config',
    'replay.mode.client': 'follows client',
    'replay.confirm': 'Apply replay',
    'replay.selected': 'Selected: {0}',
    'replay.pick': 'Pick an era card first, then press "Apply replay".',
    'replay.applied': 'Switched to era {0}.',
    'replay.baseOutfit': 'Base outfit',
    'notice.title': 'Please read this first',
    'notice.footer': 'This server is completely free. If you paid for it, ask for a refund and report the seller as described above.',
    'notice.ok': 'I understand',
    'replay.retired': 'Data removed',
    'replay.retired.tip': 'Era {0} data has been removed from the resource packs, so it cannot be replayed.',
    'replay.same': 'That era is already being served.',
    'replay.mainUi': 'Main UI',
    'replay.window': 'Window',
    'replay.filter': 'Filter era / name / character',
    'replay.save': 'Write to Config.json (survives restart)',
    'replay.off': 'Stop replay',
    'replay.hint': 'Click a card to switch to that era; tick the box to persist it into the config file.',
    'replay.inUse': 'active',
    'replay.era': 'Era {0}',
    'replay.castCount': '{0} characters · {1} outfits',
    'replay.cast': 'Characters and outfits',
    'replay.skinTitle': 'outfit',
    'replay.noMatch': 'No era matches',
    'replay.select': 'Pick a card to see its characters and outfits',
    'replay.noCast': 'No character or outfit recorded for this era (empty TryID)',
    'replay.noActivity': 'No activity linked to this era',
    'replay.switched': 'Switched to era {0}',
    'replay.stopped': 'Replay stopped',
    'replay.missing': 'Cannot find <code>dlc/dlc_activities.json</code> in the resource library (root: <code>{0}</code>).',
    'replay.longTerm': 'ongoing',
    'console.title': 'Console',
    'console.quick': 'Quick command, e.g. /money list @1',
    'console.run': 'Run',
    'console.input': 'One command per line: /money add 3 10000 @1',
    'console.clear': 'Clear output',
    'console.identity': 'Commands run as the server console, i.e. with every permission.',
    'console.commands': 'Commands ({0})',
    'console.th.cmd': 'Command',
    'console.th.usage': 'Usage',
    'console.th.desc': 'Description',
    'console.noOutput': '(no output)',
    'common.refresh': 'Refresh',
    'common.copy': 'Copy'
  },
  zh_TW: {
    'app.title': 'MikuSB 管理面板',
    'brand.sub': '私服管理',
    'nav.overview': '總覽',
    'nav.accounts': '帳號',
    'nav.online': '線上',
    'nav.replay': '活動復刻',
    'nav.console': '控制台',
    'status.connecting': '連線中…',
    'status.offline': '未連線',
    'status.running': 'v{0} · 執行 {1}',
    'status.error': '介面錯誤',
    'login.title': '需要存取權杖',
    'login.hint': '權杖保存在伺服端 <code>Config/admin-token.txt</code>（也在 <code>Config.json</code> 的 <code>HttpServer.AdminToken</code>）；啟動時會自動複製到剪貼簿，並用帶權杖的連結開啟瀏覽器。',
    'login.placeholder': '貼上權杖',
    'login.submit': '進入',
    'login.invalid': '權杖無效或已變更，請重新輸入。',
    'login.connect': '無法連線伺服端：{0}',
    'load.failed': '載入失敗',
    'dur.hm': '{0} 小時 {1} 分',
    'dur.ms': '{0} 分 {1} 秒',
    'dur.s': '{0} 秒',
    'overview.title': '總覽',
    'overview.accounts': '帳號',
    'overview.online': '線上玩家',
    'overview.commands': '控制台指令',
    'overview.uptime': '執行時間',
    'overview.server': '伺服器',
    'overview.name': '名稱',
    'overview.version': '建置版本',
    'overview.database': '資料庫',
    'overview.loaded': '已載入',
    'overview.loading': '載入中',
    'overview.note': '面板是本機回環埠上的純 HTTP 服務；帳號增刪、貨幣發放等寫入操作都經由伺服端指令層執行。',
    'accounts.title': '帳號',
    'accounts.total': '共 {0} 個',
    'accounts.search': '搜尋 UID / 使用者名稱',
    'accounts.searchBtn': '搜尋',
    'accounts.newUsername': '新帳號使用者名稱',
    'accounts.newUid': 'UID（可留空）',
    'accounts.newPassword': '密碼（可留空）',
    'accounts.create': '建立',
    'accounts.th.uid': 'UID',
    'accounts.th.user': '使用者名稱',
    'accounts.th.status': '狀態',
    'accounts.th.ban': '封禁',
    'accounts.th.perms': '權限',
    'accounts.online': '線上',
    'accounts.offline': '離線',
    'accounts.normal': '正常',
    'accounts.empty': '沒有符合的帳號',
    'accounts.prev': '上一頁',
    'accounts.next': '下一頁',
    'accounts.page': '第 {0} 頁 · 共 {1} 頁',
    'accounts.detail': '詳情',
    'accounts.delete': '刪除',
    'accounts.confirmDelete': '刪除帳號 {0}（UID {1}）？該帳號的玩家資料會一併刪除。',
    'accounts.created': '已建立 {0}（UID {1}）',
    'accounts.deleted': '已刪除 UID {0}',
    'accounts.nameRequired': '使用者名稱不能為空',
    'online.title': '線上',
    'online.count': '{0} 人在線',
    'online.refresh': '重新整理',
    'online.th.name': '角色名稱',
    'online.th.level': '等級',
    'online.th.remote': '來源位址',
    'online.empty': '目前沒有玩家在線',
    'online.unnamed': '（未命名）',
    'player.charName': '角色名稱',
    'player.level': '等級',
    'player.vigor': '體力',
    'player.items': '背包物品',
    'player.weapons': '武器',
    'player.skins': '時裝',
    'player.currencies': '貨幣（在線時可直接修改）',
    'player.query': '查詢',
    'player.add': '增加',
    'player.set': '設為',
    'player.offlineHint': '玩家離線：貨幣指令要求目標在線',
    'player.th.id': 'ID',
    'player.th.name': '名稱',
    'player.th.balance': '餘額',
    'player.noCurrency': '該帳號還沒有貨幣資料',
    'player.details': '詳情',
    'player.ban': '封禁',
    'player.registered': '註冊時間',
    'player.lastActive': '最後活躍',
    'player.none': '該帳號還沒有遊戲資料',
    'replay.title': '活動復刻',
    'replay.available': '{0} 期可選',
    'replay.current': '目前服務期號',
    'replay.none': '未固定',
    'replay.mode': '生效方式',
    'replay.mode.runtime': '執行期切換',
    'replay.mode.persisted': '設定檔固定',
    'replay.mode.client': '跟隨客戶端',
    'replay.confirm': '確定復刻',
    'replay.selected': '已選：{0}',
    'replay.pick': '先點一張期號卡，再按「確定復刻」。',
    'replay.applied': '已切換到第 {0} 期。',
    'replay.baseOutfit': '基礎外觀',
    'notice.title': '請先讀這則提醒',
    'notice.footer': '本伺服器完全免費。如果你為它付過錢，請依上面的說明退款並檢舉賣家。',
    'notice.ok': '我知道了',
    'replay.retired': '資料已刪除',
    'replay.retired.tip': '第 {0} 期的資料已從資源包中刪除，無法復刻。',
    'replay.same': '該期已在服務中。',
    'replay.mainUi': '主介面',
    'replay.window': '時間窗',
    'replay.filter': '篩選期號 / 名稱 / 角色',
    'replay.save': '寫入 Config.json（重新啟動後保留）',
    'replay.off': '取消復刻',
    'replay.hint': '點卡片即切換該期；勾選則同時寫入設定檔，不勾只影響本次執行。',
    'replay.inUse': '使用中',
    'replay.era': '第 {0} 期',
    'replay.castCount': '{0} 角色 · {1} 套時裝',
    'replay.cast': '涉及角色與時裝',
    'replay.skinTitle': '時裝',
    'replay.noMatch': '沒有符合的期號',
    'replay.select': '選一張卡片，查看該期涉及的角色與時裝',
    'replay.noCast': '該期沒有記錄到角色 / 時裝（TryID 為空）',
    'replay.noActivity': '該期沒有關聯活動',
    'replay.switched': '已切換到第 {0} 期',
    'replay.stopped': '已取消復刻',
    'replay.missing': '資源庫裡找不到 <code>dlc/dlc_activities.json</code>（目前根目錄：<code>{0}</code>）。',
    'replay.longTerm': '長期',
    'console.title': '控制台',
    'console.quick': '快捷指令，如 /money list @1',
    'console.run': '執行',
    'console.input': '一行一條：/money add 3 10000 @1',
    'console.clear': '清空輸出',
    'console.identity': '指令以伺服端控制台身分執行，擁有全部權限。',
    'console.commands': '可用指令（{0}）',
    'console.th.cmd': '指令',
    'console.th.usage': '用法',
    'console.th.desc': '說明',
    'console.noOutput': '（無輸出）',
    'common.refresh': '重新整理',
    'common.copy': '複製'
  },
  ja_JP: {
    'app.title': 'MikuSB 管理パネル',
    'brand.sub': 'サーバー管理',
    'nav.overview': '概要',
    'nav.accounts': 'アカウント',
    'nav.online': 'オンライン',
    'nav.replay': 'イベント復刻',
    'nav.console': 'コンソール',
    'status.connecting': '接続中…',
    'status.offline': '未接続',
    'status.running': 'v{0} · 稼働 {1}',
    'status.error': 'API エラー',
    'login.title': 'アクセストークンが必要です',
    'login.hint': 'トークンはサーバーの <code>Config/admin-token.txt</code>（および <code>Config.json</code> の <code>HttpServer.AdminToken</code>）にあります。起動時にクリップボードへコピーされ、トークン付きのリンクでブラウザーが開きます。',
    'login.placeholder': 'トークンを貼り付け',
    'login.submit': '入る',
    'login.invalid': 'トークンが無効か変更されています。もう一度入力してください。',
    'login.connect': 'サーバーに接続できません: {0}',
    'load.failed': '読み込みに失敗しました',
    'dur.hm': '{0} 時間 {1} 分',
    'dur.ms': '{0} 分 {1} 秒',
    'dur.s': '{0} 秒',
    'overview.title': '概要',
    'overview.accounts': 'アカウント',
    'overview.online': 'オンラインプレイヤー',
    'overview.commands': 'コンソールコマンド',
    'overview.uptime': '稼働時間',
    'overview.server': 'サーバー',
    'overview.name': '名前',
    'overview.version': 'ビルド',
    'overview.database': 'データベース',
    'overview.loaded': '読み込み済み',
    'overview.loading': '読み込み中',
    'overview.note': 'このパネルはローカルループバックポート上の素の HTTP サービスです。アカウントの作成・削除や通貨の付与はすべてサーバーのコマンド層を通ります。',
    'accounts.title': 'アカウント',
    'accounts.total': '全 {0} 件',
    'accounts.search': 'UID / ユーザー名を検索',
    'accounts.searchBtn': '検索',
    'accounts.newUsername': '新しいアカウントのユーザー名',
    'accounts.newUid': 'UID（空欄可）',
    'accounts.newPassword': 'パスワード（空欄可）',
    'accounts.create': '作成',
    'accounts.th.uid': 'UID',
    'accounts.th.user': 'ユーザー名',
    'accounts.th.status': '状態',
    'accounts.th.ban': 'BAN',
    'accounts.th.perms': '権限',
    'accounts.online': 'オンライン',
    'accounts.offline': 'オフライン',
    'accounts.normal': '正常',
    'accounts.empty': '一致するアカウントがありません',
    'accounts.prev': '前へ',
    'accounts.next': '次へ',
    'accounts.page': '{0} / {1} ページ',
    'accounts.detail': '詳細',
    'accounts.delete': '削除',
    'accounts.confirmDelete': 'アカウント {0}（UID {1}）を削除しますか？プレイヤーデータも一緒に削除されます。',
    'accounts.created': '{0}（UID {1}）を作成しました',
    'accounts.deleted': 'UID {0} を削除しました',
    'accounts.nameRequired': 'ユーザー名は必須です',
    'online.title': 'オンライン',
    'online.count': '{0} 人がオンライン',
    'online.refresh': '更新',
    'online.th.name': 'キャラクター名',
    'online.th.level': 'レベル',
    'online.th.remote': '接続元',
    'online.empty': '現在オンラインのプレイヤーはいません',
    'online.unnamed': '（名前なし）',
    'player.charName': 'キャラクター名',
    'player.level': 'レベル',
    'player.vigor': 'スタミナ',
    'player.items': '所持アイテム',
    'player.weapons': '武器',
    'player.skins': '衣装',
    'player.currencies': '通貨（オンライン中は直接変更可）',
    'player.query': '照会',
    'player.add': '加算',
    'player.set': '設定',
    'player.offlineHint': 'プレイヤーはオフラインです: 通貨コマンドはオンラインの対象が必要です',
    'player.th.id': 'ID',
    'player.th.name': '名称',
    'player.th.balance': '残高',
    'player.noCurrency': 'このアカウントにはまだ通貨データがありません',
    'player.details': '詳細',
    'player.ban': 'BAN',
    'player.registered': '登録日時',
    'player.lastActive': '最終アクセス',
    'player.none': 'このアカウントにはまだゲームデータがありません',
    'replay.title': 'イベント復刻',
    'replay.available': '{0} 期から選択',
    'replay.current': '現在の配信期',
    'replay.none': '未固定',
    'replay.mode': '適用方法',
    'replay.mode.runtime': '実行中に切替',
    'replay.mode.persisted': '設定ファイルで固定',
    'replay.mode.client': 'クライアントに追従',
    'replay.confirm': '復刻を確定',
    'replay.selected': '選択中：{0}',
    'replay.pick': '先に期のカードを選び、「復刻を確定」を押してください。',
    'replay.applied': '第 {0} 期に切り替えました。',
    'replay.baseOutfit': 'ベース衣装',
    'notice.title': 'まずこの注意書きをお読みください',
    'notice.footer': 'このサーバーは完全に無料です。料金を支払った場合は、上記の手順で返金と通報を行ってください。',
    'notice.ok': '了解しました',
    'replay.retired': 'データ削除済み',
    'replay.retired.tip': '第 {0} 期のデータはリソースパックから削除されたため、復刻できません。',
    'replay.same': 'その期はすでに配信中です。',
    'replay.mainUi': 'メイン UI',
    'replay.window': '期間',
    'replay.filter': '期 / 名前 / キャラクターで絞り込み',
    'replay.save': 'Config.json に書き込む（再起動後も維持）',
    'replay.off': '復刻を解除',
    'replay.hint': 'カードをクリックするとその期に切り替わります。チェックすると設定ファイルにも書き込まれ、外すと今回の実行だけに適用されます。',
    'replay.inUse': '適用中',
    'replay.era': '第 {0} 期',
    'replay.castCount': '{0} キャラ · {1} 衣装',
    'replay.cast': '登場キャラクターと衣装',
    'replay.skinTitle': '衣装',
    'replay.noMatch': '一致する期がありません',
    'replay.select': 'カードを選ぶと、その期のキャラクターと衣装を表示します',
    'replay.noCast': 'この期にはキャラクター / 衣装の記録がありません（TryID が空）',
    'replay.noActivity': 'この期に紐づくイベントはありません',
    'replay.switched': '第 {0} 期に切り替えました',
    'replay.stopped': '復刻を解除しました',
    'replay.missing': 'リソースライブラリに <code>dlc/dlc_activities.json</code> が見つかりません（現在のルート: <code>{0}</code>）。',
    'replay.longTerm': '長期',
    'console.title': 'コンソール',
    'console.quick': 'クイックコマンド（例: /money list @1）',
    'console.run': '実行',
    'console.input': '1 行 1 コマンド: /money add 3 10000 @1',
    'console.clear': '出力を消す',
    'console.identity': 'コマンドはサーバーコンソールとして実行され、すべての権限を持ちます。',
    'console.commands': '利用可能なコマンド（{0}）',
    'console.th.cmd': 'コマンド',
    'console.th.usage': '使い方',
    'console.th.desc': '説明',
    'console.noOutput': '（出力なし）',
    'common.refresh': '更新',
    'common.copy': 'コピー'
  },
  ko_KR: {
    'app.title': 'MikuSB 관리 패널',
    'brand.sub': '사설 서버 관리',
    'nav.overview': '개요',
    'nav.accounts': '계정',
    'nav.online': '온라인',
    'nav.replay': '활동 복각',
    'nav.console': '콘솔',
    'status.connecting': '연결 중…',
    'status.offline': '연결 안 됨',
    'status.running': 'v{0} · {1} 실행 중',
    'status.error': 'API 오류',
    'login.title': '액세스 토큰 필요',
    'login.hint': '토큰은 서버의 <code>Config/admin-token.txt</code>(및 <code>Config.json</code>의 <code>HttpServer.AdminToken</code>)에 있습니다. 시작 시 클립보드에 복사되고 토큰이 포함된 링크로 브라우저가 열립니다.',
    'login.placeholder': '토큰 붙여넣기',
    'login.submit': '들어가기',
    'login.invalid': '토큰이 잘못되었거나 변경되었습니다. 다시 입력하세요.',
    'login.connect': '서버에 연결할 수 없습니다: {0}',
    'load.failed': '불러오기 실패',
    'dur.hm': '{0}시간 {1}분',
    'dur.ms': '{0}분 {1}초',
    'dur.s': '{0}초',
    'overview.title': '개요',
    'overview.accounts': '계정',
    'overview.online': '접속 중인 플레이어',
    'overview.commands': '콘솔 명령',
    'overview.uptime': '가동 시간',
    'overview.server': '서버',
    'overview.name': '이름',
    'overview.version': '빌드 버전',
    'overview.database': '데이터베이스',
    'overview.loaded': '로드됨',
    'overview.loading': '로드 중',
    'overview.note': '패널은 로컬 루프백 포트의 순수 HTTP 서비스입니다. 계정 추가·삭제, 재화 지급 등 쓰기 작업은 모두 서버 명령 계층을 통해 실행됩니다.',
    'accounts.title': '계정',
    'accounts.total': '총 {0}개',
    'accounts.search': 'UID / 사용자 이름 검색',
    'accounts.searchBtn': '검색',
    'accounts.newUsername': '새 계정 사용자 이름',
    'accounts.newUid': 'UID (비워 둘 수 있음)',
    'accounts.newPassword': '비밀번호 (비워 둘 수 있음)',
    'accounts.create': '만들기',
    'accounts.th.uid': 'UID',
    'accounts.th.user': '사용자 이름',
    'accounts.th.status': '상태',
    'accounts.th.ban': '차단',
    'accounts.th.perms': '권한',
    'accounts.online': '온라인',
    'accounts.offline': '오프라인',
    'accounts.normal': '정상',
    'accounts.empty': '일치하는 계정이 없습니다',
    'accounts.prev': '이전',
    'accounts.next': '다음',
    'accounts.page': '{0} / {1} 페이지',
    'accounts.detail': '상세',
    'accounts.delete': '삭제',
    'accounts.confirmDelete': '계정 {0}(UID {1})을(를) 삭제할까요? 해당 계정의 플레이어 데이터도 함께 삭제됩니다.',
    'accounts.created': '{0}(UID {1}) 생성됨',
    'accounts.deleted': 'UID {0} 삭제됨',
    'accounts.nameRequired': '사용자 이름은 비워 둘 수 없습니다',
    'online.title': '온라인',
    'online.count': '{0}명 온라인',
    'online.refresh': '새로 고침',
    'online.th.name': '캐릭터 이름',
    'online.th.level': '레벨',
    'online.th.remote': '원격 주소',
    'online.empty': '지금은 접속 중인 플레이어가 없습니다',
    'online.unnamed': '(이름 없음)',
    'player.charName': '캐릭터 이름',
    'player.level': '레벨',
    'player.vigor': '기력',
    'player.items': '인벤토리 아이템',
    'player.weapons': '무기',
    'player.skins': '스킨',
    'player.currencies': '재화 (접속 중일 때 바로 수정 가능)',
    'player.query': '조회',
    'player.add': '추가',
    'player.set': '설정',
    'player.offlineHint': '플레이어 오프라인: 재화 명령은 대상이 접속 중이어야 합니다',
    'player.th.id': 'ID',
    'player.th.name': '이름',
    'player.th.balance': '잔액',
    'player.noCurrency': '이 계정에는 아직 재화 데이터가 없습니다',
    'player.details': '상세',
    'player.ban': '차단',
    'player.registered': '가입 시간',
    'player.lastActive': '마지막 활동',
    'player.none': '이 계정에는 아직 게임 데이터가 없습니다',
    'replay.title': '활동 복각',
    'replay.available': '{0}개 기간 선택 가능',
    'replay.current': '현재 서비스 기간',
    'replay.none': '고정되지 않음',
    'replay.mode': '적용 방식',
    'replay.mode.runtime': '실행 중 전환',
    'replay.mode.persisted': '설정 파일 고정',
    'replay.mode.client': '클라이언트 따름',
    'replay.confirm': '복각 확정',
    'replay.selected': '선택됨: {0}',
    'replay.pick': '먼저 기간 카드를 고른 뒤 "복각 확정"을 누르세요.',
    'replay.applied': '{0}기로 전환했습니다.',
    'replay.baseOutfit': '기본 의상',
    'notice.title': '먼저 이 안내를 읽어 주세요',
    'notice.footer': '이 서버는 완전히 무료입니다. 비용을 지불했다면 위 안내대로 환불과 신고를 진행하세요.',
    'notice.ok': '확인했습니다',
    'replay.retired': '데이터 삭제됨',
    'replay.retired.tip': '{0}기 데이터가 리소스 팩에서 삭제되어 복각할 수 없습니다.',
    'replay.same': '해당 기간은 이미 적용 중입니다.',
    'replay.mainUi': '메인 UI',
    'replay.window': '기간',
    'replay.filter': '기간 / 이름 / 캐릭터 필터',
    'replay.save': 'Config.json에 기록 (재시작 후 유지)',
    'replay.off': '복각 해제',
    'replay.hint': '카드를 클릭하면 해당 기간으로 전환됩니다. 체크하면 설정 파일에도 기록되고, 해제하면 이번 실행에만 적용됩니다.',
    'replay.inUse': '사용 중',
    'replay.era': '{0}기',
    'replay.castCount': '캐릭터 {0}명 · 스킨 {1}벌',
    'replay.cast': '등장 캐릭터와 스킨',
    'replay.skinTitle': '스킨',
    'replay.noMatch': '일치하는 기간이 없습니다',
    'replay.select': '카드를 선택하면 해당 기간의 캐릭터와 스킨을 볼 수 있습니다',
    'replay.noCast': '이 기간에는 캐릭터 / 스킨 기록이 없습니다 (TryID 비어 있음)',
    'replay.noActivity': '이 기간에 연결된 활동이 없습니다',
    'replay.switched': '{0}기로 전환했습니다',
    'replay.stopped': '복각을 해제했습니다',
    'replay.missing': '리소스 라이브러리에서 <code>dlc/dlc_activities.json</code>을 찾을 수 없습니다 (현재 루트: <code>{0}</code>).',
    'replay.longTerm': '장기',
    'console.title': '콘솔',
    'console.quick': '빠른 명령, 예: /money list @1',
    'console.run': '실행',
    'console.input': '한 줄에 하나: /money add 3 10000 @1',
    'console.clear': '출력 지우기',
    'console.identity': '명령은 서버 콘솔 권한으로 실행되며 모든 권한을 가집니다.',
    'console.commands': '사용 가능한 명령 ({0})',
    'console.th.cmd': '명령',
    'console.th.usage': '사용법',
    'console.th.desc': '설명',
    'console.noOutput': '(출력 없음)',
    'common.refresh': '새로 고침',
    'common.copy': '복사'
  },
  de_DE: {
    'app.title': 'MikuSB Admin-Panel',
    'brand.sub': 'Serververwaltung',
    'nav.overview': 'Übersicht',
    'nav.accounts': 'Konten',
    'nav.online': 'Online',
    'nav.replay': 'Era-Replay',
    'nav.console': 'Konsole',
    'status.connecting': 'Verbinde…',
    'status.offline': 'Nicht verbunden',
    'status.running': 'v{0} · läuft {1}',
    'status.error': 'API-Fehler',
    'login.title': 'Zugriffstoken erforderlich',
    'login.hint': 'Das Token liegt in <code>Config/admin-token.txt</code> (und in <code>HttpServer.AdminToken</code> der <code>Config.json</code>). Beim Start wird es in die Zwischenablage kopiert und der Browser mit dem Token geöffnet.',
    'login.placeholder': 'Token einfügen',
    'login.submit': 'Anmelden',
    'login.invalid': 'Token ungültig oder geändert – bitte erneut eingeben.',
    'login.connect': 'Keine Verbindung zum Server: {0}',
    'load.failed': 'Laden fehlgeschlagen',
    'dur.hm': '{0} Std. {1} Min.',
    'dur.ms': '{0} Min. {1} Sek.',
    'dur.s': '{0} Sek.',
    'overview.title': 'Übersicht',
    'overview.accounts': 'Konten',
    'overview.online': 'Spieler online',
    'overview.commands': 'Konsolenbefehle',
    'overview.uptime': 'Laufzeit',
    'overview.server': 'Server',
    'overview.name': 'Name',
    'overview.version': 'Build',
    'overview.database': 'Datenbank',
    'overview.loaded': 'geladen',
    'overview.loading': 'lädt',
    'overview.note': 'Das Panel ist ein reiner HTTP-Dienst auf dem lokalen Loopback-Port; Konten anlegen und löschen sowie Währungen ausgeben laufen über die Befehlsschicht des Servers.',
    'accounts.title': 'Konten',
    'accounts.total': '{0} gesamt',
    'accounts.search': 'UID / Benutzername suchen',
    'accounts.searchBtn': 'Suchen',
    'accounts.newUsername': 'Benutzername des neuen Kontos',
    'accounts.newUid': 'UID (optional)',
    'accounts.newPassword': 'Passwort (optional)',
    'accounts.create': 'Anlegen',
    'accounts.th.uid': 'UID',
    'accounts.th.user': 'Benutzername',
    'accounts.th.status': 'Status',
    'accounts.th.ban': 'Sperre',
    'accounts.th.perms': 'Rechte',
    'accounts.online': 'online',
    'accounts.offline': 'offline',
    'accounts.normal': 'normal',
    'accounts.empty': 'Keine passenden Konten',
    'accounts.prev': 'Zurück',
    'accounts.next': 'Weiter',
    'accounts.page': 'Seite {0} von {1}',
    'accounts.detail': 'Details',
    'accounts.delete': 'Löschen',
    'accounts.confirmDelete': 'Konto {0} (UID {1}) löschen? Die Spieldaten des Kontos werden mitgelöscht.',
    'accounts.created': '{0} angelegt (UID {1})',
    'accounts.deleted': 'UID {0} gelöscht',
    'accounts.nameRequired': 'Benutzername darf nicht leer sein',
    'online.title': 'Online',
    'online.count': '{0} online',
    'online.refresh': 'Aktualisieren',
    'online.th.name': 'Charaktername',
    'online.th.level': 'Stufe',
    'online.th.remote': 'Adresse',
    'online.empty': 'Zurzeit ist niemand online',
    'online.unnamed': '(ohne Namen)',
    'player.charName': 'Charaktername',
    'player.level': 'Stufe',
    'player.vigor': 'Ausdauer',
    'player.items': 'Inventar',
    'player.weapons': 'Waffen',
    'player.skins': 'Skins',
    'player.currencies': 'Währungen (bei Online-Spielern direkt änderbar)',
    'player.query': 'Abfragen',
    'player.add': 'Hinzufügen',
    'player.set': 'Setzen',
    'player.offlineHint': 'Spieler offline: Währungsbefehle verlangen ein Online-Ziel',
    'player.th.id': 'ID',
    'player.th.name': 'Name',
    'player.th.balance': 'Stand',
    'player.noCurrency': 'Dieses Konto hat noch keine Währungsdaten',
    'player.details': 'Details',
    'player.ban': 'Sperre',
    'player.registered': 'Registriert',
    'player.lastActive': 'Zuletzt aktiv',
    'player.none': 'Dieses Konto hat noch keine Spieldaten',
    'replay.title': 'Era-Replay',
    'replay.available': '{0} Epochen verfügbar',
    'replay.current': 'Aktuell bediente Epoche',
    'replay.none': 'nicht fixiert',
    'replay.mode': 'Modus',
    'replay.mode.runtime': 'Laufzeitwechsel',
    'replay.mode.persisted': 'in Konfiguration fixiert',
    'replay.mode.client': 'folgt dem Client',
    'replay.confirm': 'Anwenden',
    'replay.selected': 'Ausgewählt: {0}',
    'replay.pick': 'Wähle zuerst eine Ära-Karte und klicke dann auf „Anwenden“.',
    'replay.applied': 'Auf Ära {0} umgestellt.',
    'replay.baseOutfit': 'Basis-Outfit',
    'notice.title': 'Bitte zuerst lesen',
    'notice.footer': 'Dieser Server ist vollständig kostenlos. Wenn du dafür bezahlt hast, fordere eine Rückerstattung an und melde den Verkäufer wie oben beschrieben.',
    'notice.ok': 'Verstanden',
    'replay.retired': 'Daten entfernt',
    'replay.retired.tip': 'Die Daten von Ära {0} wurden aus den Ressourcenpaketen entfernt, ein Replay ist nicht möglich.',
    'replay.same': 'Diese Ära läuft bereits.',
    'replay.mainUi': 'Haupt-UI',
    'replay.window': 'Zeitraum',
    'replay.filter': 'Epoche / Name / Charakter filtern',
    'replay.save': 'In Config.json schreiben (bleibt nach Neustart)',
    'replay.off': 'Replay beenden',
    'replay.hint': 'Klick auf eine Karte wechselt die Epoche; mit Häkchen wird sie zusätzlich in die Konfiguration geschrieben, sonst gilt sie nur für diesen Lauf.',
    'replay.inUse': 'aktiv',
    'replay.era': 'Epoche {0}',
    'replay.castCount': '{0} Charaktere · {1} Skins',
    'replay.cast': 'Beteiligte Charaktere und Skins',
    'replay.skinTitle': 'Skin',
    'replay.noMatch': 'Keine passende Epoche',
    'replay.select': 'Karte wählen, um Charaktere und Skins dieser Epoche zu sehen',
    'replay.noCast': 'Für diese Epoche sind keine Charaktere/Skins erfasst (TryID leer)',
    'replay.noActivity': 'Keine verknüpften Aktivitäten in dieser Epoche',
    'replay.switched': 'Auf Epoche {0} gewechselt',
    'replay.stopped': 'Replay beendet',
    'replay.missing': '<code>dlc/dlc_activities.json</code> wurde im Ressourcenordner nicht gefunden (aktuelles Wurzelverzeichnis: <code>{0}</code>).',
    'replay.longTerm': 'dauerhaft',
    'console.title': 'Konsole',
    'console.quick': 'Schnellbefehl, z. B. /money list @1',
    'console.run': 'Ausführen',
    'console.input': 'Eine Zeile pro Befehl: /money add 3 10000 @1',
    'console.clear': 'Ausgabe leeren',
    'console.identity': 'Befehle laufen als Serverkonsole und haben alle Rechte.',
    'console.commands': 'Verfügbare Befehle ({0})',
    'console.th.cmd': 'Befehl',
    'console.th.usage': 'Nutzung',
    'console.th.desc': 'Beschreibung',
    'console.noOutput': '(keine Ausgabe)',
    'common.refresh': 'Aktualisieren',
    'common.copy': 'Kopieren'
  },
  fr_FR: {
    'app.title': 'Panneau d’administration MikuSB',
    'brand.sub': 'gestion du serveur',
    'nav.overview': 'Vue d’ensemble',
    'nav.accounts': 'Comptes',
    'nav.online': 'En ligne',
    'nav.replay': 'Rejeu d’ère',
    'nav.console': 'Console',
    'status.connecting': 'Connexion…',
    'status.offline': 'Hors ligne',
    'status.running': 'v{0} · actif depuis {1}',
    'status.error': 'Erreur d’API',
    'login.title': 'Jeton d’accès requis',
    'login.hint': 'Le jeton est stocké dans <code>Config/admin-token.txt</code> (et dans <code>HttpServer.AdminToken</code> de <code>Config.json</code>). Au démarrage il est copié dans le presse-papiers et le navigateur s’ouvre avec le jeton.',
    'login.placeholder': 'Coller le jeton',
    'login.submit': 'Entrer',
    'login.invalid': 'Jeton invalide ou modifié, veuillez le saisir à nouveau.',
    'login.connect': 'Impossible de joindre le serveur : {0}',
    'load.failed': 'Échec du chargement',
    'dur.hm': '{0} h {1} min',
    'dur.ms': '{0} min {1} s',
    'dur.s': '{0} s',
    'overview.title': 'Vue d’ensemble',
    'overview.accounts': 'Comptes',
    'overview.online': 'Joueurs en ligne',
    'overview.commands': 'Commandes console',
    'overview.uptime': 'Durée de service',
    'overview.server': 'Serveur',
    'overview.name': 'Nom',
    'overview.version': 'Version de build',
    'overview.database': 'Base de données',
    'overview.loaded': 'chargée',
    'overview.loading': 'chargement',
    'overview.note': 'Le panneau est un service HTTP simple sur le port de boucle locale ; la création/suppression de comptes et l’octroi de monnaie passent par la couche de commandes du serveur.',
    'accounts.title': 'Comptes',
    'accounts.total': '{0} au total',
    'accounts.search': 'Rechercher un UID / nom d’utilisateur',
    'accounts.searchBtn': 'Rechercher',
    'accounts.newUsername': 'Nom du nouveau compte',
    'accounts.newUid': 'UID (facultatif)',
    'accounts.newPassword': 'Mot de passe (facultatif)',
    'accounts.create': 'Créer',
    'accounts.th.uid': 'UID',
    'accounts.th.user': 'Nom d’utilisateur',
    'accounts.th.status': 'État',
    'accounts.th.ban': 'Bannissement',
    'accounts.th.perms': 'Droits',
    'accounts.online': 'en ligne',
    'accounts.offline': 'hors ligne',
    'accounts.normal': 'normal',
    'accounts.empty': 'Aucun compte correspondant',
    'accounts.prev': 'Précédent',
    'accounts.next': 'Suivant',
    'accounts.page': 'Page {0} sur {1}',
    'accounts.detail': 'Détails',
    'accounts.delete': 'Supprimer',
    'accounts.confirmDelete': 'Supprimer le compte {0} (UID {1}) ? Les données de jeu du compte seront aussi supprimées.',
    'accounts.created': '{0} créé (UID {1})',
    'accounts.deleted': 'UID {0} supprimé',
    'accounts.nameRequired': 'Le nom d’utilisateur ne peut pas être vide',
    'online.title': 'En ligne',
    'online.count': '{0} en ligne',
    'online.refresh': 'Actualiser',
    'online.th.name': 'Nom du personnage',
    'online.th.level': 'Niveau',
    'online.th.remote': 'Adresse',
    'online.empty': 'Aucun joueur en ligne actuellement',
    'online.unnamed': '(sans nom)',
    'player.charName': 'Nom du personnage',
    'player.level': 'Niveau',
    'player.vigor': 'Vigueur',
    'player.items': 'Objets d’inventaire',
    'player.weapons': 'Armes',
    'player.skins': 'Tenues',
    'player.currencies': 'Monnaies (modifiables si le joueur est en ligne)',
    'player.query': 'Consulter',
    'player.add': 'Ajouter',
    'player.set': 'Définir',
    'player.offlineHint': 'Joueur hors ligne : les commandes de monnaie exigent une cible en ligne',
    'player.th.id': 'ID',
    'player.th.name': 'Nom',
    'player.th.balance': 'Solde',
    'player.noCurrency': 'Ce compte n’a pas encore de données de monnaie',
    'player.details': 'Détails',
    'player.ban': 'Bannissement',
    'player.registered': 'Inscription',
    'player.lastActive': 'Dernière activité',
    'player.none': 'Ce compte n’a pas encore de données de jeu',
    'replay.title': 'Rejeu d’ère',
    'replay.available': '{0} ères disponibles',
    'replay.current': 'Ère actuellement servie',
    'replay.none': 'non figée',
    'replay.mode': 'Mode',
    'replay.mode.runtime': 'changement à chaud',
    'replay.mode.persisted': 'figée dans la configuration',
    'replay.mode.client': 'suit le client',
    'replay.confirm': 'Appliquer',
    'replay.selected': 'Sélection : {0}',
    'replay.pick': 'Choisissez d’abord une carte d’ère, puis « Appliquer ».',
    'replay.applied': 'Ère {0} appliquée.',
    'replay.baseOutfit': 'Tenue de base',
    'notice.title': 'À lire avant de continuer',
    'notice.footer': 'Ce serveur est entièrement gratuit. Si vous avez payé, demandez un remboursement et signalez le vendeur comme indiqué ci-dessus.',
    'notice.ok': 'J’ai compris',
    'replay.retired': 'Données supprimées',
    'replay.retired.tip': 'Les données de la période {0} ont été retirées des packs de ressources : rejeu impossible.',
    'replay.same': 'Cette ère est déjà diffusée.',
    'replay.mainUi': 'UI principale',
    'replay.window': 'Fenêtre',
    'replay.filter': 'Filtrer ère / nom / personnage',
    'replay.save': 'Écrire dans Config.json (conservé au redémarrage)',
    'replay.off': 'Arrêter le rejeu',
    'replay.hint': 'Cliquez sur une carte pour basculer sur cette ère ; cochez la case pour l’écrire aussi dans la configuration, sinon elle ne vaut que pour cette exécution.',
    'replay.inUse': 'active',
    'replay.era': 'Ère {0}',
    'replay.castCount': '{0} personnages · {1} tenues',
    'replay.cast': 'Personnages et tenues concernés',
    'replay.skinTitle': 'Tenue',
    'replay.noMatch': 'Aucune ère correspondante',
    'replay.select': 'Choisissez une carte pour voir les personnages et tenues de cette ère',
    'replay.noCast': 'Aucun personnage / tenue enregistré pour cette ère (TryID vide)',
    'replay.noActivity': 'Aucune activité liée à cette ère',
    'replay.switched': 'Basculé sur l’ère {0}',
    'replay.stopped': 'Rejeu arrêté',
    'replay.missing': '<code>dlc/dlc_activities.json</code> est introuvable dans la bibliothèque de ressources (racine actuelle : <code>{0}</code>).',
    'replay.longTerm': 'permanent',
    'console.title': 'Console',
    'console.quick': 'Commande rapide, ex. /money list @1',
    'console.run': 'Exécuter',
    'console.input': 'Une ligne par commande : /money add 3 10000 @1',
    'console.clear': 'Effacer la sortie',
    'console.identity': 'Les commandes s’exécutent en tant que console du serveur, avec tous les droits.',
    'console.commands': 'Commandes disponibles ({0})',
    'console.th.cmd': 'Commande',
    'console.th.usage': 'Usage',
    'console.th.desc': 'Description',
    'console.noOutput': '(aucune sortie)',
    'common.refresh': 'Actualiser',
    'common.copy': 'Copier'
  },
  es_ES: {
    'app.title': 'Panel de administración MikuSB',
    'brand.sub': 'administración del servidor',
    'nav.overview': 'Resumen',
    'nav.accounts': 'Cuentas',
    'nav.online': 'En línea',
    'nav.replay': 'Repetición de era',
    'nav.console': 'Consola',
    'status.connecting': 'Conectando…',
    'status.offline': 'Sin conexión',
    'status.running': 'v{0} · activo {1}',
    'status.error': 'Error de API',
    'login.title': 'Se requiere token de acceso',
    'login.hint': 'El token se guarda en <code>Config/admin-token.txt</code> (y en <code>HttpServer.AdminToken</code> de <code>Config.json</code>). Al arrancar se copia al portapapeles y el navegador se abre con el token.',
    'login.placeholder': 'Pega el token',
    'login.submit': 'Entrar',
    'login.invalid': 'Token no válido o modificado, vuelve a introducirlo.',
    'login.connect': 'No se puede conectar al servidor: {0}',
    'load.failed': 'Error al cargar',
    'dur.hm': '{0} h {1} min',
    'dur.ms': '{0} min {1} s',
    'dur.s': '{0} s',
    'overview.title': 'Resumen',
    'overview.accounts': 'Cuentas',
    'overview.online': 'Jugadores en línea',
    'overview.commands': 'Comandos de consola',
    'overview.uptime': 'Tiempo activo',
    'overview.server': 'Servidor',
    'overview.name': 'Nombre',
    'overview.version': 'Versión de compilación',
    'overview.database': 'Base de datos',
    'overview.loaded': 'cargada',
    'overview.loading': 'cargando',
    'overview.note': 'El panel es un servicio HTTP simple en el puerto de bucle local; crear o borrar cuentas y entregar moneda pasan por la capa de comandos del servidor.',
    'accounts.title': 'Cuentas',
    'accounts.total': '{0} en total',
    'accounts.search': 'Buscar UID / nombre de usuario',
    'accounts.searchBtn': 'Buscar',
    'accounts.newUsername': 'Nombre de la nueva cuenta',
    'accounts.newUid': 'UID (opcional)',
    'accounts.newPassword': 'Contraseña (opcional)',
    'accounts.create': 'Crear',
    'accounts.th.uid': 'UID',
    'accounts.th.user': 'Nombre de usuario',
    'accounts.th.status': 'Estado',
    'accounts.th.ban': 'Bloqueo',
    'accounts.th.perms': 'Permisos',
    'accounts.online': 'en línea',
    'accounts.offline': 'desconectado',
    'accounts.normal': 'normal',
    'accounts.empty': 'No hay cuentas coincidentes',
    'accounts.prev': 'Anterior',
    'accounts.next': 'Siguiente',
    'accounts.page': 'Página {0} de {1}',
    'accounts.detail': 'Detalles',
    'accounts.delete': 'Eliminar',
    'accounts.confirmDelete': '¿Eliminar la cuenta {0} (UID {1})? También se borrarán sus datos de juego.',
    'accounts.created': '{0} creada (UID {1})',
    'accounts.deleted': 'UID {0} eliminada',
    'accounts.nameRequired': 'El nombre de usuario no puede estar vacío',
    'online.title': 'En línea',
    'online.count': '{0} en línea',
    'online.refresh': 'Actualizar',
    'online.th.name': 'Nombre del personaje',
    'online.th.level': 'Nivel',
    'online.th.remote': 'Dirección',
    'online.empty': 'No hay jugadores en línea ahora mismo',
    'online.unnamed': '(sin nombre)',
    'player.charName': 'Nombre del personaje',
    'player.level': 'Nivel',
    'player.vigor': 'Vigor',
    'player.items': 'Objetos de inventario',
    'player.weapons': 'Armas',
    'player.skins': 'Trajes',
    'player.currencies': 'Monedas (editables si el jugador está en línea)',
    'player.query': 'Consultar',
    'player.add': 'Añadir',
    'player.set': 'Fijar',
    'player.offlineHint': 'Jugador desconectado: los comandos de moneda exigen un objetivo en línea',
    'player.th.id': 'ID',
    'player.th.name': 'Nombre',
    'player.th.balance': 'Saldo',
    'player.noCurrency': 'Esta cuenta aún no tiene datos de moneda',
    'player.details': 'Detalles',
    'player.ban': 'Bloqueo',
    'player.registered': 'Registro',
    'player.lastActive': 'Última actividad',
    'player.none': 'Esta cuenta aún no tiene datos de juego',
    'replay.title': 'Repetición de era',
    'replay.available': '{0} eras disponibles',
    'replay.current': 'Era servida actualmente',
    'replay.none': 'sin fijar',
    'replay.mode': 'Modo',
    'replay.mode.runtime': 'cambio en caliente',
    'replay.mode.persisted': 'fijada en la configuración',
    'replay.mode.client': 'sigue al cliente',
    'replay.confirm': 'Aplicar',
    'replay.selected': 'Seleccionado: {0}',
    'replay.pick': 'Elige primero una tarjeta de era y pulsa «Aplicar».',
    'replay.applied': 'Cambiado a la era {0}.',
    'replay.baseOutfit': 'Atuendo base',
    'notice.title': 'Lee esto primero',
    'notice.footer': 'Este servidor es completamente gratuito. Si pagaste por él, pide un reembolso y denuncia al vendedor como se indica arriba.',
    'notice.ok': 'Entendido',
    'replay.retired': 'Datos eliminados',
    'replay.retired.tip': 'Los datos de la era {0} se han eliminado de los paquetes de recursos: no se puede repetir.',
    'replay.same': 'Esa era ya está activa.',
    'replay.mainUi': 'UI principal',
    'replay.window': 'Ventana',
    'replay.filter': 'Filtrar era / nombre / personaje',
    'replay.save': 'Escribir en Config.json (se conserva al reiniciar)',
    'replay.off': 'Detener la repetición',
    'replay.hint': 'Haz clic en una tarjeta para cambiar a esa era; si marcas la casilla también se escribe en la configuración, si no solo afecta a esta ejecución.',
    'replay.inUse': 'activa',
    'replay.era': 'Era {0}',
    'replay.castCount': '{0} personajes · {1} trajes',
    'replay.cast': 'Personajes y trajes implicados',
    'replay.skinTitle': 'Traje',
    'replay.noMatch': 'Ninguna era coincide',
    'replay.select': 'Elige una tarjeta para ver los personajes y trajes de esa era',
    'replay.noCast': 'Esta era no tiene personajes / trajes registrados (TryID vacío)',
    'replay.noActivity': 'Esta era no tiene actividades asociadas',
    'replay.switched': 'Cambiado a la era {0}',
    'replay.stopped': 'Repetición detenida',
    'replay.missing': 'No se encuentra <code>dlc/dlc_activities.json</code> en la biblioteca de recursos (raíz actual: <code>{0}</code>).',
    'replay.longTerm': 'permanente',
    'console.title': 'Consola',
    'console.quick': 'Comando rápido, p. ej. /money list @1',
    'console.run': 'Ejecutar',
    'console.input': 'Una línea por comando: /money add 3 10000 @1',
    'console.clear': 'Borrar salida',
    'console.identity': 'Los comandos se ejecutan como consola del servidor, con todos los permisos.',
    'console.commands': 'Comandos disponibles ({0})',
    'console.th.cmd': 'Comando',
    'console.th.usage': 'Uso',
    'console.th.desc': 'Descripción',
    'console.noOutput': '(sin salida)',
    'common.refresh': 'Actualizar',
    'common.copy': 'Copiar'
  },
  th_TH: {
    'app.title': 'แผงผู้ดูแล MikuSB',
    'brand.sub': 'จัดการเซิร์ฟเวอร์',
    'nav.overview': 'ภาพรวม',
    'nav.accounts': 'บัญชี',
    'nav.online': 'ออนไลน์',
    'nav.replay': 'ย้อนยุคกิจกรรม',
    'nav.console': 'คอนโซล',
    'status.connecting': 'กำลังเชื่อมต่อ…',
    'status.offline': 'ไม่ได้เชื่อมต่อ',
    'status.running': 'v{0} · ทำงาน {1}',
    'status.error': 'API ผิดพลาด',
    'login.title': 'ต้องใช้โทเคนเข้าถึง',
    'login.hint': 'โทเคนถูกเก็บไว้ที่ <code>Config/admin-token.txt</code> ฝั่งเซิร์ฟเวอร์ (และใน <code>HttpServer.AdminToken</code> ของ <code>Config.json</code>) เมื่อเริ่มทำงานจะคัดลอกไปยังคลิปบอร์ดและเปิดเบราว์เซอร์พร้อมโทเคน',
    'login.placeholder': 'วางโทเคน',
    'login.submit': 'เข้าสู่ระบบ',
    'login.invalid': 'โทเคนไม่ถูกต้องหรือถูกเปลี่ยน กรุณากรอกใหม่',
    'login.connect': 'เชื่อมต่อเซิร์ฟเวอร์ไม่ได้: {0}',
    'load.failed': 'โหลดไม่สำเร็จ',
    'dur.hm': '{0} ชม. {1} นาที',
    'dur.ms': '{0} นาที {1} วินาที',
    'dur.s': '{0} วินาที',
    'overview.title': 'ภาพรวม',
    'overview.accounts': 'บัญชี',
    'overview.online': 'ผู้เล่นออนไลน์',
    'overview.commands': 'คำสั่งคอนโซล',
    'overview.uptime': 'เวลาทำงาน',
    'overview.server': 'เซิร์ฟเวอร์',
    'overview.name': 'ชื่อ',
    'overview.version': 'เวอร์ชันบิลด์',
    'overview.database': 'ฐานข้อมูล',
    'overview.loaded': 'โหลดแล้ว',
    'overview.loading': 'กำลังโหลด',
    'overview.note': 'แผงนี้เป็นบริการ HTTP ธรรมดาบนพอร์ตลูปแบ็กของเครื่อง การสร้าง/ลบบัญชีและการแจกสกุลเงินทำผ่านชั้นคำสั่งของเซิร์ฟเวอร์',
    'accounts.title': 'บัญชี',
    'accounts.total': 'ทั้งหมด {0}',
    'accounts.search': 'ค้นหา UID / ชื่อผู้ใช้',
    'accounts.searchBtn': 'ค้นหา',
    'accounts.newUsername': 'ชื่อผู้ใช้บัญชีใหม่',
    'accounts.newUid': 'UID (เว้นว่างได้)',
    'accounts.newPassword': 'รหัสผ่าน (เว้นว่างได้)',
    'accounts.create': 'สร้าง',
    'accounts.th.uid': 'UID',
    'accounts.th.user': 'ชื่อผู้ใช้',
    'accounts.th.status': 'สถานะ',
    'accounts.th.ban': 'แบน',
    'accounts.th.perms': 'สิทธิ์',
    'accounts.online': 'ออนไลน์',
    'accounts.offline': 'ออฟไลน์',
    'accounts.normal': 'ปกติ',
    'accounts.empty': 'ไม่พบบัญชีที่ตรงกัน',
    'accounts.prev': 'ก่อนหน้า',
    'accounts.next': 'ถัดไป',
    'accounts.page': 'หน้า {0} จาก {1}',
    'accounts.detail': 'รายละเอียด',
    'accounts.delete': 'ลบ',
    'accounts.confirmDelete': 'ลบบัญชี {0} (UID {1})? ข้อมูลผู้เล่นของบัญชีนี้จะถูกลบด้วย',
    'accounts.created': 'สร้าง {0} แล้ว (UID {1})',
    'accounts.deleted': 'ลบ UID {0} แล้ว',
    'accounts.nameRequired': 'ชื่อผู้ใช้ห้ามว่าง',
    'online.title': 'ออนไลน์',
    'online.count': 'ออนไลน์ {0} คน',
    'online.refresh': 'รีเฟรช',
    'online.th.name': 'ชื่อตัวละคร',
    'online.th.level': 'เลเวล',
    'online.th.remote': 'ที่อยู่',
    'online.empty': 'ตอนนี้ไม่มีผู้เล่นออนไลน์',
    'online.unnamed': '(ไม่มีชื่อ)',
    'player.charName': 'ชื่อตัวละคร',
    'player.level': 'เลเวล',
    'player.vigor': 'พลัง',
    'player.items': 'ไอเท็มในกระเป๋า',
    'player.weapons': 'อาวุธ',
    'player.skins': 'สกิน',
    'player.currencies': 'สกุลเงิน (แก้ได้เมื่อออนไลน์)',
    'player.query': 'สอบถาม',
    'player.add': 'เพิ่ม',
    'player.set': 'ตั้งค่า',
    'player.offlineHint': 'ผู้เล่นออฟไลน์: คำสั่งสกุลเงินต้องมีเป้าหมายออนไลน์',
    'player.th.id': 'ID',
    'player.th.name': 'ชื่อ',
    'player.th.balance': 'คงเหลือ',
    'player.noCurrency': 'บัญชีนี้ยังไม่มีข้อมูลสกุลเงิน',
    'player.details': 'รายละเอียด',
    'player.ban': 'แบน',
    'player.registered': 'เวลาสมัคร',
    'player.lastActive': 'ใช้งานล่าสุด',
    'player.none': 'บัญชีนี้ยังไม่มีข้อมูลเกม',
    'replay.title': 'ย้อนยุคกิจกรรม',
    'replay.available': 'เลือกได้ {0} ยุค',
    'replay.current': 'ยุคที่ให้บริการอยู่',
    'replay.none': 'ยังไม่ตรึง',
    'replay.mode': 'รูปแบบ',
    'replay.mode.runtime': 'สลับขณะรัน',
    'replay.mode.persisted': 'ตรึงในไฟล์ตั้งค่า',
    'replay.mode.client': 'ตามไคลเอนต์',
    'replay.confirm': 'ยืนยัน',
    'replay.selected': 'เลือกแล้ว: {0}',
    'replay.pick': 'เลือกการ์ดช่วงเวลาก่อน แล้วกด "ยืนยัน"',
    'replay.applied': 'สลับเป็นช่วงที่ {0} แล้ว',
    'replay.baseOutfit': 'ชุดพื้นฐาน',
    'notice.title': 'โปรดอ่านก่อนใช้งาน',
    'notice.footer': 'เซิร์ฟเวอร์นี้ฟรีทั้งหมด หากคุณจ่ายเงินไปแล้ว โปรดขอคืนเงินและรายงานผู้ขายตามที่ระบุด้านบน',
    'notice.ok': 'เข้าใจแล้ว',
    'replay.retired': 'ข้อมูลถูกลบแล้ว',
    'replay.retired.tip': 'ข้อมูลของยุค {0} ถูกลบออกจากชุดทรัพยากรแล้ว จึงไม่สามารถเล่นซ้ำได้',
    'replay.same': 'ช่วงนี้เปิดให้บริการอยู่แล้ว',
    'replay.mainUi': 'หน้าหลัก',
    'replay.window': 'ช่วงเวลา',
    'replay.filter': 'กรองยุค / ชื่อ / ตัวละคร',
    'replay.save': 'เขียนลง Config.json (คงอยู่หลังรีสตาร์ต)',
    'replay.off': 'หยุดย้อนยุค',
    'replay.hint': 'คลิกการ์ดเพื่อสลับไปยุคนั้น ถ้าติ๊กจะเขียนลงไฟล์ตั้งค่าด้วย ถ้าไม่ติ๊กจะมีผลเฉพาะรอบนี้',
    'replay.inUse': 'ใช้งานอยู่',
    'replay.era': 'ยุค {0}',
    'replay.castCount': '{0} ตัวละคร · {1} สกิน',
    'replay.cast': 'ตัวละครและสกินที่เกี่ยวข้อง',
    'replay.skinTitle': 'สกิน',
    'replay.noMatch': 'ไม่พบยุคที่ตรงกัน',
    'replay.select': 'เลือกการ์ดเพื่อดูตัวละครและสกินของยุคนั้น',
    'replay.noCast': 'ยุคนี้ไม่มีข้อมูลตัวละคร / สกิน (TryID ว่าง)',
    'replay.noActivity': 'ยุคนี้ไม่มีกิจกรรมที่เชื่อมโยง',
    'replay.switched': 'สลับไปยุค {0} แล้ว',
    'replay.stopped': 'หยุดย้อนยุคแล้ว',
    'replay.missing': 'ไม่พบ <code>dlc/dlc_activities.json</code> ในคลังทรัพยากร (รูทปัจจุบัน: <code>{0}</code>)',
    'replay.longTerm': 'ระยะยาว',
    'console.title': 'คอนโซล',
    'console.quick': 'คำสั่งด่วน เช่น /money list @1',
    'console.run': 'เรียกใช้',
    'console.input': 'หนึ่งบรรทัดต่อคำสั่ง: /money add 3 10000 @1',
    'console.clear': 'ล้างผลลัพธ์',
    'console.identity': 'คำสั่งทำงานในฐานะคอนโซลเซิร์ฟเวอร์ จึงมีสิทธิ์ทั้งหมด',
    'console.commands': 'คำสั่งที่ใช้ได้ ({0})',
    'console.th.cmd': 'คำสั่ง',
    'console.th.usage': 'วิธีใช้',
    'console.th.desc': 'คำอธิบาย',
    'console.noOutput': '(ไม่มีผลลัพธ์)',
    'common.refresh': 'รีเฟรช',
    'common.copy': 'คัดลอก'
  },
  id_ID: {
    'app.title': 'Panel admin MikuSB',
    'brand.sub': 'manajemen server',
    'nav.overview': 'Ringkasan',
    'nav.accounts': 'Akun',
    'nav.online': 'Daring',
    'nav.replay': 'Ulang era',
    'nav.console': 'Konsol',
    'status.connecting': 'Menghubungkan…',
    'status.offline': 'Tidak terhubung',
    'status.running': 'v{0} · aktif {1}',
    'status.error': 'Kesalahan API',
    'login.title': 'Token akses diperlukan',
    'login.hint': 'Token disimpan di <code>Config/admin-token.txt</code> pada server (juga di <code>HttpServer.AdminToken</code> pada <code>Config.json</code>). Saat mulai token disalin ke clipboard dan browser dibuka dengan token tersebut.',
    'login.placeholder': 'Tempel token',
    'login.submit': 'Masuk',
    'login.invalid': 'Token tidak valid atau berubah, silakan masukkan lagi.',
    'login.connect': 'Tidak dapat menghubungi server: {0}',
    'load.failed': 'Gagal memuat',
    'dur.hm': '{0} jam {1} mnt',
    'dur.ms': '{0} mnt {1} dtk',
    'dur.s': '{0} dtk',
    'overview.title': 'Ringkasan',
    'overview.accounts': 'Akun',
    'overview.online': 'Pemain daring',
    'overview.commands': 'Perintah konsol',
    'overview.uptime': 'Lama aktif',
    'overview.server': 'Server',
    'overview.name': 'Nama',
    'overview.version': 'Versi build',
    'overview.database': 'Basis data',
    'overview.loaded': 'termuat',
    'overview.loading': 'memuat',
    'overview.note': 'Panel ini adalah layanan HTTP biasa pada port loopback lokal; pembuatan/penghapusan akun dan pemberian mata uang dijalankan lewat lapisan perintah server.',
    'accounts.title': 'Akun',
    'accounts.total': '{0} total',
    'accounts.search': 'Cari UID / nama pengguna',
    'accounts.searchBtn': 'Cari',
    'accounts.newUsername': 'Nama pengguna akun baru',
    'accounts.newUid': 'UID (opsional)',
    'accounts.newPassword': 'Kata sandi (opsional)',
    'accounts.create': 'Buat',
    'accounts.th.uid': 'UID',
    'accounts.th.user': 'Nama pengguna',
    'accounts.th.status': 'Status',
    'accounts.th.ban': 'Blokir',
    'accounts.th.perms': 'Izin',
    'accounts.online': 'daring',
    'accounts.offline': 'luring',
    'accounts.normal': 'normal',
    'accounts.empty': 'Tidak ada akun yang cocok',
    'accounts.prev': 'Sebelumnya',
    'accounts.next': 'Berikutnya',
    'accounts.page': 'Halaman {0} dari {1}',
    'accounts.detail': 'Detail',
    'accounts.delete': 'Hapus',
    'accounts.confirmDelete': 'Hapus akun {0} (UID {1})? Data pemain akun ini juga akan terhapus.',
    'accounts.created': '{0} dibuat (UID {1})',
    'accounts.deleted': 'UID {0} dihapus',
    'accounts.nameRequired': 'Nama pengguna tidak boleh kosong',
    'online.title': 'Daring',
    'online.count': '{0} daring',
    'online.refresh': 'Muat ulang',
    'online.th.name': 'Nama karakter',
    'online.th.level': 'Level',
    'online.th.remote': 'Alamat',
    'online.empty': 'Saat ini tidak ada pemain daring',
    'online.unnamed': '(tanpa nama)',
    'player.charName': 'Nama karakter',
    'player.level': 'Level',
    'player.vigor': 'Vigor',
    'player.items': 'Item inventaris',
    'player.weapons': 'Senjata',
    'player.skins': 'Skin',
    'player.currencies': 'Mata uang (bisa diubah saat daring)',
    'player.query': 'Tanyakan',
    'player.add': 'Tambah',
    'player.set': 'Setel',
    'player.offlineHint': 'Pemain luring: perintah mata uang memerlukan target yang daring',
    'player.th.id': 'ID',
    'player.th.name': 'Nama',
    'player.th.balance': 'Saldo',
    'player.noCurrency': 'Akun ini belum punya data mata uang',
    'player.details': 'Detail',
    'player.ban': 'Blokir',
    'player.registered': 'Waktu daftar',
    'player.lastActive': 'Aktif terakhir',
    'player.none': 'Akun ini belum punya data permainan',
    'replay.title': 'Ulang era',
    'replay.available': '{0} era tersedia',
    'replay.current': 'Era yang dilayani',
    'replay.none': 'belum dipatok',
    'replay.mode': 'Mode',
    'replay.mode.runtime': 'ganti saat berjalan',
    'replay.mode.persisted': 'dipatok di konfigurasi',
    'replay.mode.client': 'mengikuti klien',
    'replay.confirm': 'Terapkan',
    'replay.selected': 'Dipilih: {0}',
    'replay.pick': 'Pilih kartu periode dulu, lalu tekan "Terapkan".',
    'replay.applied': 'Beralih ke periode {0}.',
    'replay.baseOutfit': 'Pakaian dasar',
    'notice.title': 'Mohon baca ini dulu',
    'notice.footer': 'Server ini sepenuhnya gratis. Jika Anda membayarnya, minta pengembalian dana dan laporkan penjualnya seperti dijelaskan di atas.',
    'notice.ok': 'Saya mengerti',
    'replay.retired': 'Data dihapus',
    'replay.retired.tip': 'Data era {0} sudah dihapus dari paket sumber daya, jadi tidak bisa diputar ulang.',
    'replay.same': 'Periode itu sudah aktif.',
    'replay.mainUi': 'UI utama',
    'replay.window': 'Rentang waktu',
    'replay.filter': 'Saring era / nama / karakter',
    'replay.save': 'Tulis ke Config.json (bertahan setelah mulai ulang)',
    'replay.off': 'Hentikan ulang era',
    'replay.hint': 'Klik kartu untuk beralih ke era itu; centang kotak untuk ikut menulis ke konfigurasi, jika tidak hanya berlaku untuk jalannya ini.',
    'replay.inUse': 'aktif',
    'replay.era': 'Era {0}',
    'replay.castCount': '{0} karakter · {1} skin',
    'replay.cast': 'Karakter dan skin terkait',
    'replay.skinTitle': 'Skin',
    'replay.noMatch': 'Tidak ada era yang cocok',
    'replay.select': 'Pilih kartu untuk melihat karakter dan skin era itu',
    'replay.noCast': 'Era ini tidak mencatat karakter / skin (TryID kosong)',
    'replay.noActivity': 'Tidak ada aktivitas terkait pada era ini',
    'replay.switched': 'Beralih ke era {0}',
    'replay.stopped': 'Ulang era dihentikan',
    'replay.missing': '<code>dlc/dlc_activities.json</code> tidak ditemukan di pustaka sumber daya (akar saat ini: <code>{0}</code>).',
    'replay.longTerm': 'permanen',
    'console.title': 'Konsol',
    'console.quick': 'Perintah cepat, mis. /money list @1',
    'console.run': 'Jalankan',
    'console.input': 'Satu baris satu perintah: /money add 3 10000 @1',
    'console.clear': 'Bersihkan keluaran',
    'console.identity': 'Perintah berjalan sebagai konsol server sehingga memiliki semua izin.',
    'console.commands': 'Perintah tersedia ({0})',
    'console.th.cmd': 'Perintah',
    'console.th.usage': 'Penggunaan',
    'console.th.desc': 'Keterangan',
    'console.noOutput': '(tanpa keluaran)',
    'common.refresh': 'Muat ulang',
    'common.copy': 'Salin'
  }
};

function detectLang() {
  const saved = localStorage.getItem(LANG_KEY);
  if (saved && LANGS.some((l) => l.id === saved)) return saved;
  const nav = (navigator.language || 'zh-CN').replace('-', '_');
  const exact = LANGS.find((l) => l.id.toLowerCase() === nav.toLowerCase());
  if (exact) return exact.id;
  const two = nav.slice(0, 2).toLowerCase();
  const loose = LANGS.find((l) => l.id.slice(0, 2).toLowerCase() === two);
  return loose ? loose.id : 'zh_CN';
}

const state = {
  token: localStorage.getItem(TOKEN_KEY) || '',
  lang: detectLang(),
  accounts: { page: 1, q: '', total: 0, size: 20 },
  currencies: null,
  commands: null,
  replay: null,
  replayFilter: '',
  replayEra: 0
};

// Setup-wizard copy is merged in at load time instead of being pasted into all ten tables above.
const SETUP_STRINGS = {
  "zh_CN": {
    "nav.setup": "初始化",
    "setup.title": "初始化向导",
    "setup.lead": "三步完成：选服务器区域 → 填客户端路径 → 写入配置。面板会先读出现有配置，只改你在这里确认的项。",
    "setup.region": "1 服务器区域",
    "setup.region.cn": "国服（jinshan）",
    "setup.region.cn.note": "客户端自己启动，面板只把 7 个 SDK 域名指向本机，不需要注入 DLL。",
    "setup.region.global": "国际服",
    "setup.region.global.note": "沿用上游 Loader：注入 MikuSB-Patch.dll 并走本地 SOCKS5，面板只帮你代填路径与参数。",
    "setup.path": "2 客户端路径",
    "setup.path.placeholder": "例如 D:\\Games\\Snow，或 …\\data\\game\\Game\\Binaries\\Win64\\Game.exe",
    "setup.path.check": "检查路径",
    "setup.path.detect": "自动探测",
    "setup.path.detecting": "正在扫描磁盘…",
    "setup.path.found": "找到 {0} 个候选",
    "setup.check.locate": "定位到 Game.exe",
    "setup.check.exe": "可执行文件存在",
    "setup.check.name": "文件名是 Game.exe",
    "setup.check.userdir": "-userdir 目录",
    "setup.check.paks": "Game\\Content\\Paks",
    "setup.check.install": "安装根目录",
    "setup.address": "3 对外地址",
    "setup.address.note": "局域网其他机器要连这台服务器就选本机局域网 IP；只有本机玩就选 127.0.0.1。",
    "setup.apply": "写入配置",
    "setup.applying": "正在写入…",
    "setup.done": "配置已写入",
    "setup.next": "接下来",
    "setup.current": "当前配置",
    "setup.pending": "还没有完成初始化：先选区域，再填客户端路径。",
    "setup.hosts": "CN hosts 重定向",
    "setup.hosts.on": "已写入 {0} 条",
    "setup.hosts.off": "未写入",
    "setup.hosts.already": "已是最新",
    "setup.hosts.needsAdmin": "需要管理员权限才能修改 hosts：以管理员身份重新运行本程序即可自动写入，或手动编辑 {0}，把下列 7 个域名指向本机地址。",
    "setup.hosts.removedOk": "已移除 hosts 条目",
    "setup.hosts.none": "hosts 中没有 MikuSB 的条目",
    "setup.hosts.apply": "写入 hosts",
    "setup.hosts.remove": "移除条目",
    "setup.hosts.note": "「写入配置」时会一并写入 hosts，只有失败才提示；移除条目需要管理员权限。",
    "setup.hosts.domains": "域名",
    "setup.hosts.failed": "失败：{0}",
    "setup.region.saved": "区域：{0}",
    "setup.current.region": "区域",
    "setup.current.address": "对外地址",
    "setup.current.proxy": "SOCKS5",
    "setup.launch": "启动游戏（/game）",
    "setup.launch.note": "「写入配置」只把路径和参数写进 Config.json，不会下载补丁。点下面的按钮启动游戏，Loader 会在注入前自动下载 Patch\\MikuSB-Patch.dll。",
    "setup.launch.running": "正在启动…",
    "setup.launch.done": "已启动：{0}",
    "setup.launch.failed": "启动失败：{0}",
    "setup.patch.ready": "已就绪",
    "setup.patch.missing": "还没有 Patch\\MikuSB-Patch.dll，首次用 Loader 启动时会自动下载。",
    "setup.path.required": "先填客户端路径，或点「自动探测」让面板自己找。"
  },
  "zh_TW": {
    "nav.setup": "初始化",
    "setup.title": "初始化精靈",
    "setup.lead": "三步完成：選伺服器區域 → 填客戶端路徑 → 寫入設定。面板會先讀出現有設定，只改你在這裡確認的項目。",
    "setup.region": "1 伺服器區域",
    "setup.region.cn": "國服（jinshan）",
    "setup.region.cn.note": "客戶端自行啟動，面板只把 7 個 SDK 網域指向本機，不需注入 DLL。",
    "setup.region.global": "國際服",
    "setup.region.global.note": "沿用上游 Loader：注入 MikuSB-Patch.dll 並走本機 SOCKS5，面板只幫你代填路徑與參數。",
    "setup.path": "2 客戶端路徑",
    "setup.path.placeholder": "例如 D:\\Games\\Snow，或 …\\data\\game\\Game\\Binaries\\Win64\\Game.exe",
    "setup.path.check": "檢查路徑",
    "setup.path.detect": "自動偵測",
    "setup.path.detecting": "正在掃描磁碟…",
    "setup.path.found": "找到 {0} 個候選",
    "setup.check.locate": "定位到 Game.exe",
    "setup.check.exe": "可執行檔存在",
    "setup.check.name": "檔名為 Game.exe",
    "setup.check.userdir": "-userdir 目錄",
    "setup.check.paks": "Game\\Content\\Paks",
    "setup.check.install": "安裝根目錄",
    "setup.address": "3 對外位址",
    "setup.address.note": "區網其他機器要連這台伺服器就選本機區網 IP；只有本機玩就選 127.0.0.1。",
    "setup.apply": "寫入設定",
    "setup.applying": "正在寫入…",
    "setup.done": "設定已寫入",
    "setup.next": "接下來",
    "setup.current": "目前設定",
    "setup.pending": "尚未完成初始化：先選區域，再填客戶端路徑。",
    "setup.hosts": "CN hosts 重導向",
    "setup.hosts.on": "已寫入 {0} 條",
    "setup.hosts.off": "未寫入",
    "setup.hosts.already": "已是最新",
    "setup.hosts.needsAdmin": "需要系統管理員權限才能修改 hosts：以系統管理員身分重新執行本程式即可自動寫入，或手動編輯 {0}，將下列 7 個網域指向本機位址。",
    "setup.hosts.removedOk": "已移除 hosts 項目",
    "setup.hosts.none": "hosts 中沒有 MikuSB 的項目",
    "setup.hosts.apply": "寫入 hosts",
    "setup.hosts.remove": "移除項目",
    "setup.hosts.note": "「寫入設定」時會一併寫入 hosts，只有失敗才提示；移除項目需要系統管理員權限。",
    "setup.hosts.domains": "網域",
    "setup.hosts.failed": "失敗：{0}",
    "setup.region.saved": "區域：{0}",
    "setup.current.region": "區域",
    "setup.current.address": "對外位址",
    "setup.current.proxy": "SOCKS5",
    "setup.launch": "啟動遊戲（/game）",
    "setup.launch.note": "「寫入設定」只把路徑與參數寫進 Config.json，不會下載補丁。點下方按鈕啟動遊戲，Loader 會在注入前自動下載 Patch\\MikuSB-Patch.dll。",
    "setup.launch.running": "正在啟動…",
    "setup.launch.done": "已啟動：{0}",
    "setup.launch.failed": "啟動失敗：{0}",
    "setup.patch.ready": "已就緒",
    "setup.patch.missing": "尚未有 Patch\\MikuSB-Patch.dll，首次用 Loader 啟動時會自動下載。",
    "setup.path.required": "先填客戶端路徑，或點「自動偵測」讓面板自己找。"
  },
  "ja_JP": {
    "nav.setup": "初期設定",
    "setup.title": "初期設定ウィザード",
    "setup.lead": "3 ステップ：サーバー地域 → クライアントのパス → 設定の書き込み。既存の設定を読み出し、確認した項目だけを書き換えます。",
    "setup.region": "1 サーバー地域",
    "setup.region.cn": "中国版（jinshan）",
    "setup.region.cn.note": "クライアントは通常起動。パネルは 7 つの SDK ドメインをローカルに向けるだけで、DLL は注入しません。",
    "setup.region.global": "グローバル版",
    "setup.region.global.note": "上流の Loader 方式：MikuSB-Patch.dll を注入しローカル SOCKS5 を使用。パネルはパスと引数の入力だけ補助します。",
    "setup.path": "2 クライアントのパス",
    "setup.path.placeholder": "例 D:\\Games\\Snow、または …\\data\\game\\Game\\Binaries\\Win64\\Game.exe",
    "setup.path.check": "パスを確認",
    "setup.path.detect": "自動検出",
    "setup.path.detecting": "ディスクを走査中…",
    "setup.path.found": "{0} 件の候補",
    "setup.check.locate": "Game.exe の位置",
    "setup.check.exe": "実行ファイルの存在",
    "setup.check.name": "ファイル名が Game.exe",
    "setup.check.userdir": "-userdir ディレクトリ",
    "setup.check.paks": "Game\\Content\\Paks",
    "setup.check.install": "インストール先",
    "setup.address": "3 公開アドレス",
    "setup.address.note": "LAN の他端末から接続する場合は PC の LAN IP、この PC だけで遊ぶなら 127.0.0.1。",
    "setup.apply": "設定を書き込む",
    "setup.applying": "書き込み中…",
    "setup.done": "設定を書き込みました",
    "setup.next": "次の手順",
    "setup.current": "現在の設定",
    "setup.pending": "初期設定が未完了です：地域を選び、クライアントのパスを入力してください。",
    "setup.hosts": "CN hosts リダイレクト",
    "setup.hosts.on": "{0} 件書き込み済み",
    "setup.hosts.off": "未書き込み",
    "setup.hosts.already": "最新です",
    "setup.hosts.needsAdmin": "hosts の変更には管理者権限が必要です。管理者として本プログラムを再実行すれば自動で書き込まれます。手動で {0} を編集し、次の 7 つのドメインをこの PC のアドレスに向けてもかまいません。",
    "setup.hosts.removedOk": "hosts の項目を削除しました",
    "setup.hosts.none": "hosts に MikuSB の項目はありません",
    "setup.hosts.apply": "hosts を書き込む",
    "setup.hosts.remove": "項目を削除",
    "setup.hosts.note": "「設定を書き込む」と同時に hosts も更新されます（失敗時のみ通知）。項目の削除には管理者権限が必要です。",
    "setup.hosts.domains": "ドメイン",
    "setup.hosts.failed": "失敗：{0}",
    "setup.region.saved": "地域：{0}",
    "setup.current.region": "地域",
    "setup.current.address": "公開アドレス",
    "setup.current.proxy": "SOCKS5",
    "setup.launch": "ゲーム起動（/game）",
    "setup.launch.note": "「設定を書き込む」はパスと引数を保存するだけで、パッチはダウンロードしません。下のボタンで起動すると、Loader が注入前に Patch\\MikuSB-Patch.dll を自動取得します。",
    "setup.launch.running": "起動中…",
    "setup.launch.done": "起動しました：{0}",
    "setup.launch.failed": "起動に失敗しました：{0}",
    "setup.patch.ready": "準備完了",
    "setup.patch.missing": "Patch\\MikuSB-Patch.dll がまだありません。初回の Loader 起動時に自動取得します。",
    "setup.path.required": "クライアントのパスを入力するか、「自動検出」で探してください。"
  },
  "ko_KR": {
    "nav.setup": "초기 설정",
    "setup.title": "초기 설정 마법사",
    "setup.lead": "세 단계: 서버 지역 선택 → 클라이언트 경로 입력 → 설정 저장. 기존 설정을 먼저 읽고 확인한 항목만 바꿉니다.",
    "setup.region": "1 서버 지역",
    "setup.region.cn": "중국 서버(jinshan)",
    "setup.region.cn.note": "클라이언트는 직접 실행하며, 패널은 7개 SDK 도메인만 로컬로 돌립니다. DLL 주입은 없습니다.",
    "setup.region.global": "글로벌 서버",
    "setup.region.global.note": "업스트림 Loader 방식: MikuSB-Patch.dll 주입 + 로컬 SOCKS5. 패널은 경로와 인자만 채워 줍니다.",
    "setup.path": "2 클라이언트 경로",
    "setup.path.placeholder": "예: D:\\Games\\Snow 또는 …\\data\\game\\Game\\Binaries\\Win64\\Game.exe",
    "setup.path.check": "경로 확인",
    "setup.path.detect": "자동 탐지",
    "setup.path.detecting": "디스크 검색 중…",
    "setup.path.found": "후보 {0}개",
    "setup.check.locate": "Game.exe 위치",
    "setup.check.exe": "실행 파일 존재",
    "setup.check.name": "파일 이름이 Game.exe",
    "setup.check.userdir": "-userdir 디렉터리",
    "setup.check.paks": "Game\\Content\\Paks",
    "setup.check.install": "설치 루트",
    "setup.address": "3 공개 주소",
    "setup.address.note": "LAN의 다른 PC에서 접속하려면 이 PC의 LAN IP, 혼자 할 때는 127.0.0.1.",
    "setup.apply": "설정 저장",
    "setup.applying": "저장 중…",
    "setup.done": "설정을 저장했습니다",
    "setup.next": "다음 단계",
    "setup.current": "현재 설정",
    "setup.pending": "초기 설정이 끝나지 않았습니다: 지역을 고르고 클라이언트 경로를 입력하세요.",
    "setup.hosts": "CN hosts 리다이렉트",
    "setup.hosts.on": "{0}개 기록됨",
    "setup.hosts.off": "기록 없음",
    "setup.hosts.already": "최신 상태입니다",
    "setup.hosts.needsAdmin": "hosts를 수정하려면 관리자 권한이 필요합니다. 관리자 권한으로 다시 실행하면 자동으로 기록됩니다. 또는 {0}을 직접 편집해 다음 7개 도메인을 이 PC 주소로 지정하세요.",
    "setup.hosts.removedOk": "hosts 항목을 제거했습니다",
    "setup.hosts.none": "hosts에 MikuSB 항목이 없습니다",
    "setup.hosts.apply": "hosts 기록",
    "setup.hosts.remove": "항목 제거",
    "setup.hosts.note": "「설정 저장」시 hosts도 함께 기록되며 실패할 때만 알립니다. 항목 제거에는 관리자 권한이 필요합니다.",
    "setup.hosts.domains": "도메인",
    "setup.hosts.failed": "실패: {0}",
    "setup.region.saved": "지역: {0}",
    "setup.current.region": "지역",
    "setup.current.address": "공개 주소",
    "setup.current.proxy": "SOCKS5",
    "setup.launch": "게임 실행(/game)",
    "setup.launch.note": "\"설정 저장\"은 경로와 인자만 기록하며 패치는 내려받지 않습니다. 아래 버튼으로 실행하면 Loader가 주입 전에 Patch\\MikuSB-Patch.dll 을 자동으로 받습니다.",
    "setup.launch.running": "실행 중…",
    "setup.launch.done": "실행됨: {0}",
    "setup.launch.failed": "실행 실패: {0}",
    "setup.patch.ready": "준비됨",
    "setup.patch.missing": "Patch\\MikuSB-Patch.dll 이 아직 없습니다. Loader 첫 실행 시 자동으로 내려받습니다.",
    "setup.path.required": "클라이언트 경로를 입력하거나 \"자동 탐지\"를 눌러 주세요."
  },
  "en_US": {
    "nav.setup": "Setup",
    "setup.title": "Setup wizard",
    "setup.lead": "Three steps: pick the server region, point at the client, write the config. The panel reads your current config first and only rewrites what you confirm here.",
    "setup.region": "1 Server region",
    "setup.region.cn": "China (jinshan)",
    "setup.region.cn.note": "The client launches itself; the panel only points seven SDK domains at this machine, no DLL injection.",
    "setup.region.global": "Global",
    "setup.region.global.note": "Upstream loader path: inject MikuSB-Patch.dll and use the local SOCKS5 proxy. The panel just pre-fills the paths and arguments.",
    "setup.path": "2 Client path",
    "setup.path.placeholder": "e.g. D:\\Games\\Snow, or …\\data\\game\\Game\\Binaries\\Win64\\Game.exe",
    "setup.path.check": "Check path",
    "setup.path.detect": "Auto-detect",
    "setup.path.detecting": "Scanning drives…",
    "setup.path.found": "{0} candidate(s) found",
    "setup.check.locate": "Game.exe located",
    "setup.check.exe": "Executable exists",
    "setup.check.name": "File name is Game.exe",
    "setup.check.userdir": "-userdir folder",
    "setup.check.paks": "Game\\Content\\Paks",
    "setup.check.install": "Install root",
    "setup.address": "3 Public address",
    "setup.address.note": "Pick this machine's LAN IP if other PCs will join, or 127.0.0.1 for local-only play.",
    "setup.apply": "Write config",
    "setup.applying": "Writing…",
    "setup.done": "Config written",
    "setup.next": "Next steps",
    "setup.current": "Current config",
    "setup.pending": "Setup is not finished yet: choose a region and fill in the client path.",
    "setup.hosts": "CN hosts redirect",
    "setup.hosts.on": "{0} entries present",
    "setup.hosts.off": "not written",
    "setup.hosts.already": "up to date",
    "setup.hosts.needsAdmin": "Editing hosts needs administrator rights. Run this program as administrator to write the block automatically, or edit {0} by hand and point the 7 domains below at this machine.",
    "setup.hosts.removedOk": "Removed the hosts entries",
    "setup.hosts.none": "No MikuSB entries in hosts",
    "setup.hosts.apply": "Write hosts",
    "setup.hosts.remove": "Remove entries",
    "setup.hosts.note": "The \"write config\" button also maintains hosts and only reports failures. Removing entries needs administrator rights.",
    "setup.hosts.domains": "Domains",
    "setup.hosts.failed": "Failed: {0}",
    "setup.region.saved": "Region: {0}",
    "setup.current.region": "Region",
    "setup.current.address": "Public address",
    "setup.current.proxy": "SOCKS5",
    "setup.launch": "Launch game (/game)",
    "setup.launch.note": "\"Write config\" only stores the path and arguments; it never downloads the patch. Press the button below to start the client - the loader fetches Patch\\MikuSB-Patch.dll before injecting.",
    "setup.launch.running": "Launching…",
    "setup.launch.done": "Launched: {0}",
    "setup.launch.failed": "Launch failed: {0}",
    "setup.patch.ready": "Ready",
    "setup.patch.missing": "Patch\\MikuSB-Patch.dll is not downloaded yet; the loader fetches it on first start.",
    "setup.path.required": "Enter the client path, or press \"Detect\" to let the panel find it."
  },
  "fr_FR": {
    "nav.setup": "Installation",
    "setup.title": "Assistant d’installation",
    "setup.lead": "Trois étapes : région du serveur, chemin du client, écriture de la configuration. Le panneau lit d’abord la configuration existante et ne réécrit que ce que vous validez.",
    "setup.region": "1 Région du serveur",
    "setup.region.cn": "Chine (jinshan)",
    "setup.region.cn.note": "Le client se lance seul ; le panneau ne redirige que sept domaines SDK vers cette machine, sans injection de DLL.",
    "setup.region.global": "International",
    "setup.region.global.note": "Méthode du loader d’origine : injection de MikuSB-Patch.dll et proxy SOCKS5 local. Le panneau ne fait que pré-remplir les chemins.",
    "setup.path": "2 Chemin du client",
    "setup.path.placeholder": "ex. D:\\Games\\Snow, ou …\\data\\game\\Game\\Binaries\\Win64\\Game.exe",
    "setup.path.check": "Vérifier le chemin",
    "setup.path.detect": "Détection auto",
    "setup.path.detecting": "Analyse des disques…",
    "setup.path.found": "{0} candidat(s)",
    "setup.check.locate": "Game.exe localisé",
    "setup.check.exe": "Exécutable présent",
    "setup.check.name": "Nom du fichier Game.exe",
    "setup.check.userdir": "Dossier -userdir",
    "setup.check.paks": "Game\\Content\\Paks",
    "setup.check.install": "Racine d’installation",
    "setup.address": "3 Adresse publique",
    "setup.address.note": "Choisissez l’IP LAN de cette machine si d’autres PC se connectent, sinon 127.0.0.1.",
    "setup.apply": "Écrire la configuration",
    "setup.applying": "Écriture…",
    "setup.done": "Configuration écrite",
    "setup.next": "Étapes suivantes",
    "setup.current": "Configuration actuelle",
    "setup.pending": "Installation incomplète : choisissez une région puis le chemin du client.",
    "setup.hosts": "Redirection hosts CN",
    "setup.hosts.on": "{0} entrées présentes",
    "setup.hosts.off": "non écrit",
    "setup.hosts.already": "à jour",
    "setup.hosts.needsAdmin": "Modifier hosts demande les droits administrateur. Relancez ce programme en tant qu’administrateur pour écrire le bloc automatiquement, ou modifiez {0} à la main en pointant les 7 domaines ci-dessous vers cette machine.",
    "setup.hosts.removedOk": "Entrées hosts supprimées",
    "setup.hosts.none": "Aucune entrée MikuSB dans hosts",
    "setup.hosts.apply": "Écrire hosts",
    "setup.hosts.remove": "Supprimer les entrées",
    "setup.hosts.note": "Le bouton « écrire la configuration » met aussi à jour hosts et ne signale que les échecs. La suppression des entrées demande les droits administrateur.",
    "setup.hosts.domains": "Domaines",
    "setup.hosts.failed": "Échec : {0}",
    "setup.region.saved": "Région : {0}",
    "setup.current.region": "Région",
    "setup.current.address": "Adresse publique",
    "setup.current.proxy": "SOCKS5",
    "setup.launch": "Lancer le jeu (/game)",
    "setup.launch.note": "« Écrire la configuration » ne fait qu’enregistrer le chemin et les arguments ; aucun téléchargement. Le bouton ci-dessous démarre le client : le loader récupère Patch\\MikuSB-Patch.dll avant l’injection.",
    "setup.launch.running": "Lancement…",
    "setup.launch.done": "Lancé : {0}",
    "setup.launch.failed": "Échec du lancement : {0}",
    "setup.patch.ready": "Prêt",
    "setup.patch.missing": "Patch\\MikuSB-Patch.dll est absent ; le loader le télécharge au premier démarrage.",
    "setup.path.required": "Saisissez le chemin du client, ou utilisez « Détecter »."
  },
  "es_ES": {
    "nav.setup": "Configuración",
    "setup.title": "Asistente de configuración",
    "setup.lead": "Tres pasos: región del servidor, ruta del cliente y escritura de la configuración. El panel lee primero tu configuración y solo reescribe lo que confirmes.",
    "setup.region": "1 Región del servidor",
    "setup.region.cn": "China (jinshan)",
    "setup.region.cn.note": "El cliente se inicia solo; el panel solo redirige siete dominios SDK a este equipo, sin inyectar DLL.",
    "setup.region.global": "Internacional",
    "setup.region.global.note": "Ruta del loader original: inyecta MikuSB-Patch.dll y usa el proxy SOCKS5 local. El panel solo rellena las rutas.",
    "setup.path": "2 Ruta del cliente",
    "setup.path.placeholder": "p. ej. D:\\Games\\Snow, o …\\data\\game\\Game\\Binaries\\Win64\\Game.exe",
    "setup.path.check": "Comprobar ruta",
    "setup.path.detect": "Detección automática",
    "setup.path.detecting": "Analizando discos…",
    "setup.path.found": "{0} candidato(s)",
    "setup.check.locate": "Game.exe localizado",
    "setup.check.exe": "El ejecutable existe",
    "setup.check.name": "El archivo es Game.exe",
    "setup.check.userdir": "Carpeta -userdir",
    "setup.check.paks": "Game\\Content\\Paks",
    "setup.check.install": "Raíz de instalación",
    "setup.address": "3 Dirección pública",
    "setup.address.note": "Elige la IP LAN de este equipo si se conectarán otros PC; si juegas solo, 127.0.0.1.",
    "setup.apply": "Guardar configuración",
    "setup.applying": "Guardando…",
    "setup.done": "Configuración guardada",
    "setup.next": "Siguientes pasos",
    "setup.current": "Configuración actual",
    "setup.pending": "La configuración está incompleta: elige una región y la ruta del cliente.",
    "setup.hosts": "Redirección hosts CN",
    "setup.hosts.on": "{0} entradas presentes",
    "setup.hosts.off": "sin escribir",
    "setup.hosts.already": "al día",
    "setup.hosts.needsAdmin": "Modificar hosts requiere permisos de administrador. Ejecuta este programa como administrador para escribir el bloque automáticamente, o edita {0} a mano y apunta los 7 dominios de abajo a este equipo.",
    "setup.hosts.removedOk": "Entradas de hosts eliminadas",
    "setup.hosts.none": "No hay entradas de MikuSB en hosts",
    "setup.hosts.apply": "Escribir hosts",
    "setup.hosts.remove": "Quitar entradas",
    "setup.hosts.note": "El botón «escribir configuración» también mantiene hosts y solo avisa de los fallos. Quitar entradas requiere permisos de administrador.",
    "setup.hosts.domains": "Dominios",
    "setup.hosts.failed": "Error: {0}",
    "setup.region.saved": "Región: {0}",
    "setup.current.region": "Región",
    "setup.current.address": "Dirección pública",
    "setup.current.proxy": "SOCKS5",
    "setup.launch": "Iniciar juego (/game)",
    "setup.launch.note": "«Escribir configuración» solo guarda la ruta y los argumentos; no descarga nada. El botón de abajo inicia el cliente y el loader descarga Patch\\MikuSB-Patch.dll antes de inyectar.",
    "setup.launch.running": "Iniciando…",
    "setup.launch.done": "Iniciado: {0}",
    "setup.launch.failed": "Error al iniciar: {0}",
    "setup.patch.ready": "Listo",
    "setup.patch.missing": "Falta Patch\\MikuSB-Patch.dll; el loader lo descarga al iniciar.",
    "setup.path.required": "Introduce la ruta del cliente o pulsa «Detectar»."
  },
  "de_DE": {
    "nav.setup": "Einrichtung",
    "setup.title": "Einrichtungsassistent",
    "setup.lead": "Drei Schritte: Serverregion, Client-Pfad, Konfiguration schreiben. Das Panel liest die bestehende Konfiguration und ändert nur, was du hier bestätigst.",
    "setup.region": "1 Serverregion",
    "setup.region.cn": "China (jinshan)",
    "setup.region.cn.note": "Der Client startet selbst; das Panel zeigt nur sieben SDK-Domains auf diesen Rechner, ohne DLL-Injektion.",
    "setup.region.global": "International",
    "setup.region.global.note": "Upstream-Loader: MikuSB-Patch.dll injizieren und lokalen SOCKS5 nutzen. Das Panel füllt nur Pfade und Argumente vor.",
    "setup.path": "2 Client-Pfad",
    "setup.path.placeholder": "z. B. D:\\Games\\Snow oder …\\data\\game\\Game\\Binaries\\Win64\\Game.exe",
    "setup.path.check": "Pfad prüfen",
    "setup.path.detect": "Automatisch suchen",
    "setup.path.detecting": "Laufwerke werden durchsucht…",
    "setup.path.found": "{0} Kandidat(en)",
    "setup.check.locate": "Game.exe gefunden",
    "setup.check.exe": "Programmdatei vorhanden",
    "setup.check.name": "Dateiname ist Game.exe",
    "setup.check.userdir": "-userdir-Ordner",
    "setup.check.paks": "Game\\Content\\Paks",
    "setup.check.install": "Installationsordner",
    "setup.address": "3 Öffentliche Adresse",
    "setup.address.note": "LAN-IP dieses Rechners, wenn andere PCs mitspielen; nur lokal: 127.0.0.1.",
    "setup.apply": "Konfiguration schreiben",
    "setup.applying": "Wird geschrieben…",
    "setup.done": "Konfiguration geschrieben",
    "setup.next": "Nächste Schritte",
    "setup.current": "Aktuelle Konfiguration",
    "setup.pending": "Einrichtung unvollständig: Region wählen und Client-Pfad eintragen.",
    "setup.hosts": "CN-hosts-Umleitung",
    "setup.hosts.on": "{0} Einträge vorhanden",
    "setup.hosts.off": "nicht geschrieben",
    "setup.hosts.already": "aktuell",
    "setup.hosts.needsAdmin": "Das Ändern der hosts-Datei erfordert Administratorrechte. Starte das Programm als Administrator, um den Block automatisch zu schreiben, oder bearbeite {0} von Hand und richte die 7 Domains unten auf diesen Rechner.",
    "setup.hosts.removedOk": "hosts-Einträge entfernt",
    "setup.hosts.none": "Keine MikuSB-Einträge in hosts",
    "setup.hosts.apply": "hosts schreiben",
    "setup.hosts.remove": "Einträge entfernen",
    "setup.hosts.note": "Die Schaltfläche „Konfiguration schreiben“ pflegt auch hosts und meldet nur Fehler. Das Entfernen der Einträge erfordert Administratorrechte.",
    "setup.hosts.domains": "Domains",
    "setup.hosts.failed": "Fehlgeschlagen: {0}",
    "setup.region.saved": "Region: {0}",
    "setup.current.region": "Region",
    "setup.current.address": "Öffentliche Adresse",
    "setup.current.proxy": "SOCKS5",
    "setup.launch": "Spiel starten (/game)",
    "setup.launch.note": "„Konfiguration schreiben“ speichert nur Pfad und Argumente, es lädt nichts herunter. Der Button unten startet den Client; der Loader holt Patch\\MikuSB-Patch.dll vor dem Injizieren.",
    "setup.launch.running": "Startet…",
    "setup.launch.done": "Gestartet: {0}",
    "setup.launch.failed": "Start fehlgeschlagen: {0}",
    "setup.patch.ready": "Bereit",
    "setup.patch.missing": "Patch\\MikuSB-Patch.dll fehlt noch; der Loader lädt sie beim ersten Start.",
    "setup.path.required": "Client-Pfad eingeben oder „Erkennen“ drücken."
  },
  "th_TH": {
    "nav.setup": "ตั้งค่าเริ่มต้น",
    "setup.title": "ตัวช่วยตั้งค่าเริ่มต้น",
    "setup.lead": "สามขั้นตอน: เลือกภูมิภาคเซิร์ฟเวอร์ → ระบุที่อยู่ไคลเอนต์ → บันทึกการตั้งค่า แผงจะอ่านค่าที่มีอยู่ก่อนและแก้เฉพาะรายการที่คุณยืนยัน",
    "setup.region": "1 ภูมิภาคเซิร์ฟเวอร์",
    "setup.region.cn": "จีน (jinshan)",
    "setup.region.cn.note": "ไคลเอนต์เปิดเอง แผงเพียงชี้ 7 โดเมน SDK มาที่เครื่องนี้ ไม่มีการฉีด DLL",
    "setup.region.global": "สากล",
    "setup.region.global.note": "ใช้วิธี Loader เดิม: ฉีด MikuSB-Patch.dll และใช้ SOCKS5 ในเครื่อง แผงเพียงกรอกพาธและพารามิเตอร์ให้",
    "setup.path": "2 ที่อยู่ไคลเอนต์",
    "setup.path.placeholder": "เช่น D:\\Games\\Snow หรือ …\\data\\game\\Game\\Binaries\\Win64\\Game.exe",
    "setup.path.check": "ตรวจสอบพาธ",
    "setup.path.detect": "ค้นหาอัตโนมัติ",
    "setup.path.detecting": "กำลังสแกนดิสก์…",
    "setup.path.found": "พบ {0} รายการ",
    "setup.check.locate": "พบ Game.exe",
    "setup.check.exe": "มีไฟล์โปรแกรม",
    "setup.check.name": "ชื่อไฟล์คือ Game.exe",
    "setup.check.userdir": "โฟลเดอร์ -userdir",
    "setup.check.paks": "Game\\Content\\Paks",
    "setup.check.install": "โฟลเดอร์ติดตั้ง",
    "setup.address": "3 ที่อยู่สาธารณะ",
    "setup.address.note": "เลือก IP แลนของเครื่องนี้หากเครื่องอื่นจะเชื่อมต่อ หากเล่นคนเดียวเลือก 127.0.0.1",
    "setup.apply": "บันทึกการตั้งค่า",
    "setup.applying": "กำลังบันทึก…",
    "setup.done": "บันทึกการตั้งค่าแล้ว",
    "setup.next": "ขั้นตอนถัดไป",
    "setup.current": "การตั้งค่าปัจจุบัน",
    "setup.pending": "ยังตั้งค่าไม่เสร็จ: เลือกภูมิภาคและกรอกที่อยู่ไคลเอนต์",
    "setup.hosts": "เปลี่ยนเส้นทาง hosts (CN)",
    "setup.hosts.on": "มี {0} รายการ",
    "setup.hosts.off": "ยังไม่เขียน",
    "setup.hosts.already": "เป็นปัจจุบันแล้ว",
    "setup.hosts.needsAdmin": "การแก้ hosts ต้องใช้สิทธิ์ผู้ดูแลระบบ เรียกโปรแกรมนี้ในฐานะผู้ดูแลระบบเพื่อเขียนให้อัตโนมัติ หรือแก้ {0} ด้วยตนเองโดยชี้ 7 โดเมนด้านล่างมาที่เครื่องนี้",
    "setup.hosts.removedOk": "ลบรายการ hosts แล้ว",
    "setup.hosts.none": "ไม่มีรายการ MikuSB ใน hosts",
    "setup.hosts.apply": "เขียน hosts",
    "setup.hosts.remove": "ลบรายการ",
    "setup.hosts.note": "ปุ่ม \"เขียนการตั้งค่า\" จะอัปเดต hosts ให้ด้วย และแจ้งเฉพาะเมื่อล้มเหลว การลบรายการต้องใช้สิทธิ์ผู้ดูแลระบบ",
    "setup.hosts.domains": "โดเมน",
    "setup.hosts.failed": "ล้มเหลว: {0}",
    "setup.region.saved": "ภูมิภาค: {0}",
    "setup.current.region": "ภูมิภาค",
    "setup.current.address": "ที่อยู่สาธารณะ",
    "setup.current.proxy": "SOCKS5",
    "setup.launch": "เปิดเกม (/game)",
    "setup.launch.note": "\"บันทึกการตั้งค่า\" เก็บเฉพาะพาธและพารามิเตอร์ ไม่ได้ดาวน์โหลดแพตช์ ปุ่มด้านล่างจะเปิดไคลเอนต์ และ Loader จะดาวน์โหลด Patch\\MikuSB-Patch.dll ก่อนฉีด",
    "setup.launch.running": "กำลังเปิด…",
    "setup.launch.done": "เปิดแล้ว: {0}",
    "setup.launch.failed": "เปิดไม่สำเร็จ: {0}",
    "setup.patch.ready": "พร้อม",
    "setup.patch.missing": "ยังไม่มี Patch\\MikuSB-Patch.dll ตัว Loader จะดาวน์โหลดให้เมื่อเริ่มครั้งแรก",
    "setup.path.required": "กรอกพาธของไคลเอนต์ หรือกด \"ตรวจหาอัตโนมัติ\""
  },
  "id_ID": {
    "nav.setup": "Penyiapan",
    "setup.title": "Panduan penyiapan",
    "setup.lead": "Tiga langkah: pilih wilayah server, isi lokasi klien, tulis konfigurasi. Panel membaca konfigurasi yang ada lebih dulu dan hanya mengubah yang Anda konfirmasi.",
    "setup.region": "1 Wilayah server",
    "setup.region.cn": "Tiongkok (jinshan)",
    "setup.region.cn.note": "Klien dijalankan sendiri; panel hanya mengarahkan tujuh domain SDK ke mesin ini, tanpa menyuntikkan DLL.",
    "setup.region.global": "Internasional",
    "setup.region.global.note": "Cara loader asli: suntik MikuSB-Patch.dll dan pakai SOCKS5 lokal. Panel hanya mengisikan lokasi dan argumen.",
    "setup.path": "2 Lokasi klien",
    "setup.path.placeholder": "mis. D:\\Games\\Snow, atau …\\data\\game\\Game\\Binaries\\Win64\\Game.exe",
    "setup.path.check": "Periksa lokasi",
    "setup.path.detect": "Deteksi otomatis",
    "setup.path.detecting": "Memindai drive…",
    "setup.path.found": "{0} kandidat ditemukan",
    "setup.check.locate": "Game.exe ditemukan",
    "setup.check.exe": "Berkas program ada",
    "setup.check.name": "Nama berkas Game.exe",
    "setup.check.userdir": "Folder -userdir",
    "setup.check.paks": "Game\\Content\\Paks",
    "setup.check.install": "Akar instalasi",
    "setup.address": "3 Alamat publik",
    "setup.address.note": "Pilih IP LAN mesin ini bila PC lain ikut bermain; jika hanya lokal, pilih 127.0.0.1.",
    "setup.apply": "Tulis konfigurasi",
    "setup.applying": "Menulis…",
    "setup.done": "Konfigurasi ditulis",
    "setup.next": "Langkah berikutnya",
    "setup.current": "Konfigurasi saat ini",
    "setup.pending": "Penyiapan belum selesai: pilih wilayah lalu isi lokasi klien.",
    "setup.hosts": "Pengalihan hosts CN",
    "setup.hosts.on": "{0} entri ada",
    "setup.hosts.off": "belum ditulis",
    "setup.hosts.already": "sudah terbaru",
    "setup.hosts.needsAdmin": "Mengubah hosts memerlukan hak administrator. Jalankan program ini sebagai administrator agar blok ditulis otomatis, atau edit {0} secara manual dan arahkan 7 domain di bawah ke mesin ini.",
    "setup.hosts.removedOk": "Entri hosts dihapus",
    "setup.hosts.none": "Tidak ada entri MikuSB di hosts",
    "setup.hosts.apply": "Tulis hosts",
    "setup.hosts.remove": "Hapus entri",
    "setup.hosts.note": "Tombol \"tulis konfigurasi\" juga memperbarui hosts dan hanya memberi tahu jika gagal. Menghapus entri memerlukan hak administrator.",
    "setup.hosts.domains": "Domain",
    "setup.hosts.failed": "Gagal: {0}",
    "setup.region.saved": "Wilayah: {0}",
    "setup.current.region": "Wilayah",
    "setup.current.address": "Alamat publik",
    "setup.current.proxy": "SOCKS5",
    "setup.launch": "Jalankan game (/game)",
    "setup.launch.note": "\"Tulis konfigurasi\" hanya menyimpan jalur dan argumen, tidak mengunduh patch. Tombol di bawah menjalankan klien; loader mengunduh Patch\\MikuSB-Patch.dll sebelum menyuntik.",
    "setup.launch.running": "Menjalankan…",
    "setup.launch.done": "Dijalankan: {0}",
    "setup.launch.failed": "Gagal menjalankan: {0}",
    "setup.patch.ready": "Siap",
    "setup.patch.missing": "Patch\\MikuSB-Patch.dll belum ada; loader akan mengunduhnya saat pertama dijalankan.",
    "setup.path.required": "Isi jalur klien, atau tekan \"Deteksi otomatis\"."
  }
};
Object.keys(SETUP_STRINGS).forEach((lang) => {
  STRINGS[lang] = Object.assign(STRINGS[lang] || {}, SETUP_STRINGS[lang]);
});

function t(key, ...args) {
  const table = STRINGS[state.lang] || {};
  let value = table[key];
  if (value === undefined) value = STRINGS.en_US[key];
  if (value === undefined) value = STRINGS.zh_CN[key] !== undefined ? STRINGS.zh_CN[key] : key;
  return args.length ? value.replace(/\{(\d)\}/g, (m, i) => (args[i] === undefined ? m : String(args[i]))) : value;
}

const view = document.getElementById('view');
const statusEl = document.getElementById('status');
const loginEl = document.getElementById('login');
const tabsEl = document.getElementById('tabs');

function esc(value) {
  return String(value === undefined || value === null ? '' : value)
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;');
}

function toast(message, isError) {
  let el = document.getElementById('toast');
  if (!el) {
    el = document.createElement('div');
    el.id = 'toast';
    el.className = 'toast';
    document.body.appendChild(el);
  }
  el.textContent = message || '';
  el.classList.toggle('error', !!isError);
  el.classList.add('show');
  clearTimeout(toast.timer);
  toast.timer = setTimeout(() => el.classList.remove('show'), 3200);
}

function showLogin(message) {
  loginEl.classList.remove('hidden');
  document.getElementById('login-error').textContent = message || '';
}

function hideLogin() {
  loginEl.classList.add('hidden');
}

async function api(path, options = {}) {
  const init = { method: options.method || 'GET', headers: { Authorization: 'Bearer ' + state.token } };
  if (options.body !== undefined) {
    init.headers['Content-Type'] = 'application/json';
    init.body = JSON.stringify(options.body);
  }
  // Every call site passes a bare route ("/health"); the controller lives under /api on the same host.
  const response = await fetch('/api' + path, init);
  if (response.status === 401) {
    state.token = '';
    localStorage.removeItem(TOKEN_KEY);
    showLogin(t('login.invalid'));
    throw new Error('unauthorized');
  }
  const text = await response.text();
  let data = null;
  try {
    data = text ? JSON.parse(text) : null;
  } catch (error) {
    data = { message: text };
  }
  if (!response.ok) {
    const error = new Error((data && (data.message || data.code)) || ('HTTP ' + response.status));
    error.status = response.status;
    error.body = data;
    throw error;
  }
  return data;
}

function setStatus(ok, text) {
  statusEl.textContent = text;
  statusEl.classList.toggle('bad', !ok);
}

async function refreshStatus() {
  try {
    const health = await api('/health');
    setStatus(true, t('status.running', health.version || '?', formatDuration(health.uptime_sec || 0)));
  } catch (error) {
    if (error.message !== 'unauthorized') setStatus(false, t('status.offline'));
  }
}

// ---------------------------------------------------------------------------------------------
// shell (nav, language picker, static strings)
// ---------------------------------------------------------------------------------------------

function applyShell() {
  document.documentElement.lang = state.lang.replace('_', '-');
  document.title = t('app.title');

  const select = document.getElementById('lang');
  select.innerHTML = '';
  LANGS.forEach((lang) => {
    const option = document.createElement('option');
    option.value = lang.id;
    option.textContent = lang.label;
    select.appendChild(option);
  });
  select.value = state.lang;
  select.addEventListener('change', () => {
    localStorage.setItem(LANG_KEY, select.value);
    location.reload();
  });

  tabsEl.innerHTML = [
    ['setup', 'nav.setup'],
    ['overview', 'nav.overview'],
    ['accounts', 'nav.accounts'],
    ['online', 'nav.online'],
    ['replay', 'nav.replay'],
    ['console', 'nav.console']
  ]
    .map(([name, key]) => '<a href="#/' + name + '" data-view="' + name + '">' + esc(t(key)) + '</a>')
    .join('');

  document.querySelectorAll('[data-i18n]').forEach((el) => {
    el.textContent = t(el.getAttribute('data-i18n'));
  });
  document.getElementById('login-hint').innerHTML = t('login.hint');
  document.getElementById('login-token').placeholder = t('login.placeholder');
}

function setActiveTab(name) {
  Array.from(tabsEl.querySelectorAll('a')).forEach((a) => {
    a.classList.toggle('active', a.dataset.view === name);
  });
}

// ---------------------------------------------------------------------------------------------
// overview
// ---------------------------------------------------------------------------------------------

// ---------------------------------------------------------------------------------------------
// One-time notice
// ---------------------------------------------------------------------------------------------
// Shown once per browser (the key is versioned so a future wording change can re-arm it). It is not
// dismissible by clicking the backdrop: the point is that nobody pays for this server by accident.
const NOTICE_KEY = 'mikush.admin.notice.v1';

const NOTICE_TEXT = {
  en: 'MikuSB is completely free and open source. If anyone sold you this server or charged money to provide it, that was a scam. Request a refund immediately and report the seller to us on Discord with any relevant proof or purchase details.',
  zh: 'MikuSB 完全免费且开源。如果有人向您出售此服务器或收取费用提供此服务器，那就是诈骗。请立即申请退款，并在 Discord 上向我们举报卖家，并提供任何相关的证据或购买详情。'
};

function maybeShowNotice() {
  if (localStorage.getItem(NOTICE_KEY)) return;
  if (document.getElementById('notice-mask')) return;

  const mask = document.createElement('div');
  mask.id = 'notice-mask';
  mask.className = 'notice-mask';
  mask.innerHTML =
    '<div class="notice-card" role="dialog" aria-modal="true" aria-labelledby="notice-title">' +
    '<h2 id="notice-title">' + esc(t('notice.title')) + '</h2>' +
    '<p class="notice-en">' + esc(NOTICE_TEXT.en) + '</p>' +
    '<p class="notice-zh">' + esc(NOTICE_TEXT.zh) + '</p>' +
    '<p class="note">' + esc(t('notice.footer')) + '</p>' +
    '<button type="button" id="notice-ok">' + esc(t('notice.ok')) + '</button>' +
    '</div>';
  document.body.appendChild(mask);

  document.getElementById('notice-ok').addEventListener('click', () => {
    localStorage.setItem(NOTICE_KEY, new Date().toISOString());
    mask.remove();
  });
  document.getElementById('notice-ok').focus();
}

async function renderOverview() {
  const [overview, health] = await Promise.all([api('/overview'), api('/health')]);
  setActiveTab('overview');
  view.innerHTML = `
    <h2>${esc(t('overview.title'))}</h2>
    <div class="cards">
      <div class="card"><div class="card-label">${esc(t('overview.accounts'))}</div><div class="card-value">${esc(overview.accounts)}</div></div>
      <div class="card"><div class="card-label">${esc(t('overview.online'))}</div><div class="card-value">${esc(overview.online)}</div></div>
      <div class="card"><div class="card-label">${esc(t('overview.commands'))}</div><div class="card-value">${esc(overview.commands)}</div></div>
      <div class="card"><div class="card-label">${esc(t('overview.uptime'))}</div><div class="card-value">${esc(formatDuration(health.uptime_sec || 0))}</div></div>
    </div>
    <h3 class="section">${esc(t('overview.server'))}</h3>
    <table class="kv">
      <tr><th>${esc(t('overview.name'))}</th><td>${esc(overview.name || health.name || 'MikuSB')}</td></tr>
      <tr><th>${esc(t('overview.version'))}</th><td>${esc(health.version || '?')}</td></tr>
      <tr><th>${esc(t('overview.database'))}</th><td>${health.db_loaded
        ? '<span class="pill ok">' + esc(t('overview.loaded')) + '</span>'
        : '<span class="pill warn">' + esc(t('overview.loading')) + '</span>'}</td></tr>
    </table>
    <p class="note">${t('overview.note')}</p>
  `;
  setStatus(true, t('status.running', health.version || '?', formatDuration(health.uptime_sec || 0)));
}

// ---------------------------------------------------------------------------------------------
// accounts
// ---------------------------------------------------------------------------------------------

async function renderAccounts(page) {
  setActiveTab('accounts');
  if (page !== undefined) state.accounts.page = page;
  const { q, size } = state.accounts;
  const data = await api('/accounts?q=' + encodeURIComponent(q) + '&page=' + state.accounts.page + '&size=' + size);
  state.accounts.total = data.total;
  const pages = Math.max(1, Math.ceil(data.total / size));

  const rows = data.items
    .map(
      (account) => `
      <tr>
        <td class="mono">${esc(account.uid)}</td>
        <td>${esc(account.username)}</td>
        <td>${account.online ? '<span class="pill ok">' + esc(t('accounts.online')) + '</span>' : '<span class="pill">' + esc(t('accounts.offline')) + '</span>'}</td>
        <td>${account.ban_type && account.ban_type !== 'None' ? '<span class="pill bad">' + esc(account.ban_type) + '</span>' : esc(t('accounts.normal'))}</td>
        <td class="mono small">${esc((account.permissions || []).join(', '))}</td>
        <td class="right">
          <a class="link" href="#/players/${esc(account.uid)}">${esc(t('accounts.detail'))}</a>
          <button class="ghost danger" data-del="${esc(account.uid)}" data-name="${esc(account.username)}">${esc(t('accounts.delete'))}</button>
        </td>
      </tr>`
    )
    .join('');

  view.innerHTML = `
    <h2>${esc(t('accounts.title'))} <span class="muted">${esc(t('accounts.total', data.total))}</span></h2>
    <div class="toolbar">
      <input id="q" type="search" placeholder="${esc(t('accounts.search'))}" value="${esc(q)}">
      <button id="search">${esc(t('accounts.searchBtn'))}</button>
    </div>
    <div class="toolbar create">
      <input id="new-username" placeholder="${esc(t('accounts.newUsername'))}" autocomplete="off">
      <input id="new-uid" placeholder="${esc(t('accounts.newUid'))}" inputmode="numeric" autocomplete="off">
      <input id="new-password" type="password" placeholder="${esc(t('accounts.newPassword'))}" autocomplete="new-password">
      <button id="create">${esc(t('accounts.create'))}</button>
    </div>
    <table class="grid">
      <thead><tr>
        <th>${esc(t('accounts.th.uid'))}</th>
        <th>${esc(t('accounts.th.user'))}</th>
        <th>${esc(t('accounts.th.status'))}</th>
        <th>${esc(t('accounts.th.ban'))}</th>
        <th>${esc(t('accounts.th.perms'))}</th>
        <th></th>
      </tr></thead>
      <tbody>${rows || '<tr><td colspan="6" class="empty">' + esc(t('accounts.empty')) + '</td></tr>'}</tbody>
    </table>
    <div class="pager">
      <button id="prev" ${state.accounts.page <= 1 ? 'disabled' : ''}>${esc(t('accounts.prev'))}</button>
      <span class="muted">${esc(t('accounts.page', state.accounts.page, pages))}</span>
      <button id="next" ${state.accounts.page >= pages ? 'disabled' : ''}>${esc(t('accounts.next'))}</button>
    </div>
  `;

  document.getElementById('search').onclick = () => {
    state.accounts.q = document.getElementById('q').value.trim();
    state.accounts.page = 1;
    renderAccounts(1).catch(reportError);
  };
  document.getElementById('q').addEventListener('keydown', (event) => {
    if (event.key === 'Enter') document.getElementById('search').click();
  });
  document.getElementById('create').onclick = async () => {
    const username = document.getElementById('new-username').value.trim();
    const uid = document.getElementById('new-uid').value.trim();
    const password = document.getElementById('new-password').value;
    if (!username) {
      toast(t('accounts.nameRequired'), true);
      return;
    }
    try {
      const created = await api('/accounts', { method: 'POST', body: { username: username, uid: uid ? Number(uid) : 0, password: password } });
      toast(t('accounts.created', created.username, created.uid));
      await renderAccounts(1);
    } catch (error) {
      toast(error.message, true);
    }
  };
  document.getElementById('prev').onclick = () => renderAccounts(state.accounts.page - 1).catch(reportError);
  document.getElementById('next').onclick = () => renderAccounts(state.accounts.page + 1).catch(reportError);
  view.querySelectorAll('[data-del]').forEach((button) => {
    button.onclick = async () => {
      const uid = Number(button.dataset.del);
      if (!confirm(t('accounts.confirmDelete', button.dataset.name, uid))) return;
      try {
        await api('/accounts/' + uid, { method: 'DELETE' });
        toast(t('accounts.deleted', uid));
        await renderAccounts();
      } catch (error) {
        toast(error.message, true);
      }
    };
  });
}

// ---------------------------------------------------------------------------------------------
// online
// ---------------------------------------------------------------------------------------------

async function renderOnline() {
  setActiveTab('online');
  const data = await api('/online');
  const rows = (data.items || [])
    .map(
      (player) => `
      <tr>
        <td class="mono"><a class="link" href="#/players/${esc(player.uid)}">${esc(player.uid)}</a></td>
        <td>${esc(player.name || t('online.unnamed'))}</td>
        <td class="mono">${esc(player.level)}</td>
        <td class="mono small">${esc(player.remote_address || '')}</td>
      </tr>`
    )
    .join('');

  view.innerHTML = `
    <h2>${esc(t('online.title'))} <span class="muted">${esc(t('online.count', data.count || 0))}</span></h2>
    <div class="toolbar"><button id="refresh">${esc(t('online.refresh'))}</button></div>
    <table class="grid">
      <thead><tr>
        <th>${esc(t('accounts.th.uid'))}</th>
        <th>${esc(t('online.th.name'))}</th>
        <th>${esc(t('online.th.level'))}</th>
        <th>${esc(t('online.th.remote'))}</th>
      </tr></thead>
      <tbody>${rows || '<tr><td colspan="4" class="empty">' + esc(t('online.empty')) + '</td></tr>'}</tbody>
    </table>
  `;
  document.getElementById('refresh').onclick = () => renderOnline().catch(reportError);
}

// ---------------------------------------------------------------------------------------------
// player detail
// ---------------------------------------------------------------------------------------------

async function renderPlayer(uid) {
  setActiveTab('online');
  const [data, currencies] = await Promise.all([api('/players/' + uid), loadCurrencies()]);
  const account = data.account;
  const player = data.player;
  const inventory = data.inventory || {};
  const list = data.currencies || [];
  const names = new Map(currencies.map((item) => [item.id, item.name]));

  const rows = list
    .map(
      (row) => `
      <tr>
        <td class="mono">${esc(row.id)}</td>
        <td>${esc(row.name || names.get(row.id) || '#' + row.id)}</td>
        <td class="mono">${esc(row.value)}</td>
      </tr>`
    )
    .join('');

  view.innerHTML = `
    <h2>UID ${esc(uid)} · ${esc((account && account.username) || '?')} ${account && account.online ? '<span class="pill ok">' + esc(t('accounts.online')) + '</span>' : '<span class="pill">' + esc(t('accounts.offline')) + '</span>'}</h2>
    <div class="cards">
      <div class="card"><div class="card-label">${esc(t('player.charName'))}</div><div class="card-value small">${esc((player && player.name) || '—')}</div></div>
      <div class="card"><div class="card-label">${esc(t('player.level'))}</div><div class="card-value">${esc((player && player.level) ?? '—')}</div></div>
      <div class="card"><div class="card-label">${esc(t('player.vigor'))}</div><div class="card-value">${esc((player && player.vigor) ?? '—')}</div></div>
      <div class="card"><div class="card-label">${esc(t('player.items'))}</div><div class="card-value">${esc(inventory.items || 0)}</div></div>
    </div>
    <h3 class="section">${esc(t('player.details'))}</h3>
    <table class="kv">
      <tr><th>${esc(t('player.ban'))}</th><td>${esc((account && account.ban_type) || 'None')}</td></tr>
      <tr><th>${esc(t('player.weapons'))} / ${esc(t('player.skins'))}</th><td class="mono">${esc(inventory.weapons || 0)} / ${esc(inventory.skins || 0)}</td></tr>
      <tr><th>${esc(t('player.registered'))}</th><td>${esc(formatDate(player && player.register_time))}</td></tr>
      <tr><th>${esc(t('player.lastActive'))}</th><td>${esc(formatDate(player && player.last_active_time))}</td></tr>
    </table>
    <h3 class="section">${esc(t('player.currencies'))}</h3>
    <div class="toolbar">
      <select id="money-id">
        ${currencies.map((item) => '<option value="' + esc(item.id) + '">' + esc(item.id + ' · ' + item.name) + '</option>').join('')}
      </select>
      <input id="money-value" type="number" value="1000" min="0" step="1">
      <button id="money-get">${esc(t('player.query'))}</button>
      <button id="money-add">${esc(t('player.add'))}</button>
      <button id="money-set">${esc(t('player.set'))}</button>
    </div>
    ${account && account.online ? '' : '<p class="note">' + esc(t('player.offlineHint')) + '</p>'}
    <table class="grid">
      <thead><tr>
        <th>${esc(t('player.th.id'))}</th>
        <th>${esc(t('player.th.name'))}</th>
        <th>${esc(t('player.th.balance'))}</th>
      </tr></thead>
      <tbody>${rows || '<tr><td colspan="3" class="empty">' + esc(t('player.noCurrency')) + '</td></tr>'}</tbody>
    </table>
  `;

  const run = async (text) => {
    try {
      const result = await api('/console', { method: 'POST', body: { command: text } });
      toast((result.messages || []).join('\n') || t('replay.stopped'));
      await renderPlayer(uid);
    } catch (error) {
      toast(error.message, true);
    }
  };
  document.getElementById('money-get').onclick = () => run('/money get ' + document.getElementById('money-id').value + ' @' + uid);
  document.getElementById('money-add').onclick = () =>
    run('/money add ' + document.getElementById('money-id').value + ' ' + (document.getElementById('money-value').value || '0') + ' @' + uid);
  document.getElementById('money-set').onclick = () =>
    run('/money set ' + document.getElementById('money-id').value + ' ' + (document.getElementById('money-value').value || '0') + ' @' + uid);
}

async function loadCurrencies() {
  if (!state.currencies) state.currencies = await api('/currencies');
  return state.currencies;
}

// ---------------------------------------------------------------------------------------------
// era replay
// ---------------------------------------------------------------------------------------------

async function renderReplay() {
  setActiveTab('replay');
  const data = await api('/replay?lang=' + encodeURIComponent(state.lang));
  state.replay = data;

  if (!data.available) {
    view.innerHTML = '<h2>' + esc(t('replay.title')) + '</h2><p class="note">' + t('replay.missing', esc(data.root || '')) + '</p>';
    return;
  }

  const eras = data.eras || [];
  const current = data.current || { id: 0 };
  const currentEra = eras.find((era) => era.id === current.id) || null;
  const mode = current.runtime_pin
    ? t('replay.mode.runtime')
    : current.persisted
      ? t('replay.mode.persisted')
      : t('replay.mode.client');

  // The selection is a pending choice; the served era only changes when the operator confirms it.
  if (!eras.some((era) => era.id === state.replaySelected && !era.retired)) {
    const firstPick = eras.find((era) => !era.retired);
    state.replaySelected = current.id || (firstPick ? firstPick.id : 0);
  }

  view.innerHTML = `
    <h2>${esc(t('replay.title'))} <span class="muted">${esc(t('replay.available', eras.length))}</span></h2>
    <div class="cards">
      <div class="card"><div class="card-label">${esc(t('replay.current'))}</div><div class="card-value">${current.id ? esc(eraLabel(currentEra, current.id)) : esc(t('replay.none'))}</div></div>
      <div class="card"><div class="card-label">${esc(t('replay.mode'))}</div><div class="card-value small">${esc(mode)}</div></div>
      <div class="card"><div class="card-label">${esc(t('replay.window'))}</div><div class="card-value small">${esc(currentEra ? formatRange(currentEra.start_unix, currentEra.end_unix) : '—')}</div></div>
    </div>
    <div class="toolbar">
      <input id="replay-filter" type="search" placeholder="${esc(t('replay.filter'))}" value="${esc(state.replayFilter)}">
      <label class="check"><input id="replay-save" type="checkbox" checked> ${esc(t('replay.save'))}</label>
      <button id="replay-off" class="ghost" ${current.id ? '' : 'disabled'}>${esc(t('replay.off'))}</button>
    </div>
    <div class="replay-apply">
      <span id="replay-selected" class="muted small"></span>
      <button id="replay-apply" class="primary">${esc(t('replay.confirm'))}</button>
    </div>
    <p class="note">${esc(t('replay.pick'))}</p>
    <div class="era-grid" id="era-grid"></div>
    <div id="era-detail"></div>
  `;

  const grid = document.getElementById('era-grid');
  const detail = document.getElementById('era-detail');
  const applyBtn = document.getElementById('replay-apply');
  const selectedLabel = document.getElementById('replay-selected');

  const matches = (era) => {
    const needle = state.replayFilter.trim().toLowerCase();
    if (!needle) return true;
    const haystack = [era.id, era.name || '']
      .concat((era.characters || []).map((chara) => (chara.name || '') + ' ' + (chara.skin || '')))
      .join(' ')
      .toLowerCase();
    return haystack.indexOf(needle) !== -1;
  };

  const paintCast = (era) => {
    if (!era) {
      detail.innerHTML = '<p class="note">' + esc(t('replay.select')) + '</p>';
      return;
    }
    const cast = era.characters || [];
    const groups = [];
    const byNumber = new Map();
    cast.forEach((chara) => {
      let group = byNumber.get(chara.number);
      if (!group) {
        group = { number: chara.number, key: chara.key, name: chara.name, outfits: [] };
        byNumber.set(chara.number, group);
        groups.push(group);
      }
      group.outfits.push(chara);
    });

    const body = groups.length
      ? '<div class="cast">' +
        groups
          .map(
            (group) => `
            <div class="cast-group">
              <div class="cast-head">
                <span class="cast-name">${esc(group.name || ('#' + group.number))}</span>
                <span class="mono muted small">${esc(String(group.key || '').replace(/^girl0*/, '#'))}</span>
              </div>
              <div class="chips">
                ${group.outfits
                  .map(
                    (chara) => `<span class="chip" title="act ${esc(chara.activity_id)}">${esc(chara.skin || t('replay.baseOutfit'))}<span class="chip-var">${esc(chara.variant || '')}</span></span>`
                  )
                  .join('')}
              </div>
            </div>`
          )
          .join('') +
        '</div>'
      : '<p class="note">' + esc(t('replay.noCast')) + '</p>';

    detail.innerHTML = `
      <h3 class="section">${esc(eraTitle(era))}</h3>
      <div class="sub">${esc(t('replay.castCount', groups.length, cast.length))} · ${esc(formatRange(era.start_unix, era.end_unix))}</div>
      ${body}
    `;
  };

  const syncApply = () => {
    const selected = eras.find((era) => era.id === state.replaySelected) || null;
    selectedLabel.textContent = selected ? t('replay.selected', eraLabel(selected, selected.id)) : '';
    const same = !!current.id && state.replaySelected === current.id;
    const retired = !!selected && selected.retired;
    applyBtn.disabled = !selected || same || retired;
    applyBtn.textContent = same ? t('replay.same') : retired ? t('replay.retired') : t('replay.confirm');
  };

  const paint = () => {
    const visible = eras.filter(matches);
    grid.innerHTML = visible.length
      ? visible
          .map((era) => {
            const active = era.id === current.id;
            const selected = era.id === state.replaySelected;
            const cast = era.characters || [];
            const characters = new Set(cast.map((chara) => chara.number)).size;
            return `
              <button class="era-card${active ? ' active' : ''}${selected ? ' selected' : ''}${era.retired ? ' retired' : ''}" data-era="${esc(era.id)}"${era.retired ? ' data-retired="1"' : ''}>
                <span class="era-thumb${era.has_image ? '' : ' empty'}" data-img="${esc(era.id)}"></span>
                <div class="era-top">
                  <span class="era-id">${esc(t('replay.era', era.id))}</span>
                  ${active ? '<span class="pill ok">' + esc(t('replay.inUse')) + '</span>' : ''}
                  ${era.retired ? '<span class="pill muted">' + esc(t('replay.retired')) + '</span>' : ''}
                </div>
                <div class="era-name">${esc(era.name || '')}</div>
                <div class="era-win">${esc(formatRange(era.start_unix, era.end_unix))}</div>
                <div class="era-count">${esc(t('replay.castCount', characters, cast.length))}</div>
              </button>`;
          })
          .join('')
      : '<p class="note">' + esc(t('replay.noMatch')) + '</p>';

    grid.querySelectorAll('[data-era]').forEach((node) => {
      node.onclick = () => {
        const id = Number(node.dataset.era);
        // Withdrawn eras keep their card as a record of what the server used to ship, but there is no data
        // left to serve: say so instead of selecting a card whose confirm button can never work.
        if (node.dataset.retired) {
          toast(t('replay.retired.tip', id), true);
          return;
        }
        state.replaySelected = id;
        paint();
        paintCast(eras.find((era) => era.id === state.replaySelected) || null);
      };
    });

    grid.querySelectorAll('[data-img]').forEach((node) => {
      eraImage(Number(node.dataset.img)).then((url) => {
        if (!url) {
          node.classList.add('empty');
          return;
        }
        node.style.backgroundImage = 'url("' + url + '")';
      });
    });

    syncApply();
  };

  const stopReplay = async () => {
    try {
      await api('/replay', { method: 'POST', body: { id: 0, save: true } });
      toast(t('replay.stopped'));
      await renderReplay();
    } catch (error) {
      toast(error.message, true);
    }
  };

  applyBtn.onclick = async () => {
    const id = state.replaySelected;
    if (!id) return;
    const label = applyBtn.textContent;
    applyBtn.disabled = true;
    applyBtn.textContent = '…';
    try {
      const save = document.getElementById('replay-save').checked;
      const result = await api('/replay', { method: 'POST', body: { id: id, save: save } });
      toast(t('replay.applied', id));
      if ((result.messages || []).length) console.log(result.messages.join('\n'));
      await renderReplay();
    } catch (error) {
      toast(error.message, true);
      applyBtn.disabled = false;
      applyBtn.textContent = label;
    }
  };

  document.getElementById('replay-filter').addEventListener('input', (event) => {
    state.replayFilter = event.target.value;
    paint();
  });
  document.getElementById('replay-off').onclick = stopReplay;

  paint();
  paintCast(eras.find((era) => era.id === state.replaySelected) || null);
}

// Key art is served behind the bearer token, so an <img src> cannot load it: fetch once, keep the blob URL.
function eraImage(id) {
  if (!state.eraImages) state.eraImages = new Map();
  if (state.eraImages.has(id)) return state.eraImages.get(id);

  const pending = (async () => {
    try {
      const response = await fetch('/api/replay/image/' + id, {
        headers: { Authorization: 'Bearer ' + state.token }
      });
      if (!response.ok) return null;
      return URL.createObjectURL(await response.blob());
    } catch (error) {
      return null;
    }
  })();

  state.eraImages.set(id, pending);
  return pending;
}
function eraLabel(era, id) {
  const base = t('replay.era', id);
  return era && era.name ? base + ' · ' + era.name : base;
}

function eraTitle(era) {
  return era.name ? t('replay.era', era.id) + ' · ' + era.name : t('replay.era', era.id);
}

// ---------------------------------------------------------------------------------------------
// console
// ---------------------------------------------------------------------------------------------

async function renderConsole(prefill) {
  setActiveTab('console');
  if (!state.commands) {
    // The endpoint answers { items: [...] } (like /accounts), so unwrap it here; older builds returned a
    // bare array and a .map() over the wrapper threw "commands.map is not a function", which left the
    // replay page on screen and looked like the nav link did nothing.
    const payload = await api('/commands');
    state.commands = Array.isArray(payload) ? payload : (payload && payload.items) || [];
  }
  const commands = state.commands || [];

  view.innerHTML = `
    <h2>${esc(t('console.title'))}</h2>
    <p class="note">${esc(t('console.identity'))}</p>
    <div class="toolbar">
      <input id="cmd-quick" placeholder="${esc(t('console.quick'))}" value="${esc(prefill || '')}">
      <button id="cmd-run">${esc(t('console.run'))}</button>
      <button id="cmd-clear" class="ghost">${esc(t('console.clear'))}</button>
    </div>
    <textarea id="cmd-batch" rows="4" placeholder="${esc(t('console.input'))}"></textarea>
    <pre id="cmd-output" class="output"></pre>
    <h3 class="section">${esc(t('console.commands', commands.length))}</h3>
    <table class="grid">
      <thead><tr>
        <th>${esc(t('console.th.cmd'))}</th>
        <th>${esc(t('console.th.usage'))}</th>
        <th>${esc(t('console.th.desc'))}</th>
      </tr></thead>
      <tbody>
        ${commands
          .map(
            (cmd) => `
          <tr>
            <td class="mono">${esc(cmd.name)}</td>
            <td class="mono small">${esc(cmd.usage || '')}</td>
            <td class="small">${esc(cmd.description || '')}</td>
          </tr>`
          )
          .join('')}
      </tbody>
    </table>
  `;

  const output = document.getElementById('cmd-output');
  const append = (text, isError) => {
    const line = document.createElement('div');
    line.className = isError ? 'err' : '';
    line.textContent = text;
    output.appendChild(line);
    output.scrollTop = output.scrollHeight;
  };

  const execute = async (command) => {
    const text = (command || '').trim();
    if (!text) return;
    append('> ' + text);
    try {
      const result = await api('/console', { method: 'POST', body: { command: text } });
      const messages = result.messages || [];
      if (!messages.length) append(t('console.noOutput'));
      messages.forEach((message) => append(message));
    } catch (error) {
      append(error.message, true);
    }
  };

  document.getElementById('cmd-run').onclick = () => {
    const quick = document.getElementById('cmd-quick');
    const batch = document.getElementById('cmd-batch');
    const lines = (batch.value || '').split('\n').map((line) => line.trim()).filter(Boolean);
    if (lines.length) {
      batch.value = '';
      lines.reduce((chain, line) => chain.then(() => execute(line)), Promise.resolve());
    } else {
      execute(quick.value);
      quick.value = '';
    }
  };
  document.getElementById('cmd-clear').onclick = () => {
    output.innerHTML = '';
  };
}

// ---------------------------------------------------------------------------------------------
// formatting + routing
// ---------------------------------------------------------------------------------------------

// ---------------------------------------------------------------------------------------------
// setup wizard
// ---------------------------------------------------------------------------------------------

function setupChecks(probe) {
  if (!probe || !probe.checks || !probe.checks.length) {
    return '<p class="note">' + esc(t('setup.path.hint')) + '</p>';
  }
  return (
    '<ul class="checks">' +
    probe.checks
      .map(
        (check) =>
          '<li class="' + (check.ok ? 'ok' : 'bad') + '">' +
          '<span class="pill ' + (check.ok ? 'ok' : 'warn') + '">' + (check.ok ? 'OK' : '!') + '</span> ' +
          '<b>' + esc(t('setup.check.' + check.key)) + '</b> ' +
          '<span class="mono">' + esc(check.detail || '') + '</span>' +
          '</li>'
      )
      .join('') +
    '</ul>'
  );
}

function setupRegionLabel(id) {
  return id === 'cn' ? t('setup.region.cn') : t('setup.region.global');
}

async function renderSetup() {
  const data = await api('/setup');
  setActiveTab('setup');
  state.setupData = data;
  if (!state.setupRegion) state.setupRegion = data.region || 'global';

  const addresses = (data.addresses && data.addresses.length ? data.addresses : ['127.0.0.1']).slice();
  // The configured address is not necessarily one of the host's own NICs (it can be a public IP the
  // operator typed by hand), so keep it in the list instead of silently defaulting to the first entry.
  if (data.public_address && addresses.indexOf(data.public_address) < 0) {
    addresses.unshift(data.public_address);
  }
  if (!state.setupAddress) {
    state.setupAddress = addresses.indexOf(data.public_address) >= 0 ? data.public_address : addresses[0];
  }

  const probe = data.probe || { valid: false, checks: [] };
  const region = state.setupRegion;
  const hosts = data.hosts || { entries: [], managed: false };

  view.innerHTML = `
    <h2>${esc(t('setup.title'))}</h2>
    <p class="note">${esc(t('setup.lead'))}</p>
    ${probe.valid ? '' : '<div class="callout warn">' + esc(t('setup.pending')) + '</div>'}

    <h3 class="section">${esc(t('setup.region'))}</h3>
    <div class="region-row">
      ${(data.regions || [])
        .map(
          (item) =>
            '<button class="region-card' + (item.id === region ? ' active' : '') + '" data-region="' + esc(item.id) + '">' +
            '<span class="region-name">' + esc(item.id === 'cn' ? t('setup.region.cn') : t('setup.region.global')) + '</span>' +
            '<span class="region-note">' + esc(item.id === 'cn' ? t('setup.region.cn.note') : t('setup.region.global.note')) + '</span>' +
            '<span class="region-tag mono">' + esc(item.loader) + '</span>' +
            '</button>'
        )
        .join('')}
    </div>

    <h3 class="section">${esc(t('setup.path'))}</h3>
    <div class="setup-row">
      <input id="setup-path" class="grow mono" spellcheck="false" placeholder="${esc(t('setup.path.placeholder'))}" value="${esc(data.game_path || '')}">
      <button id="setup-check" class="secondary">${esc(t('setup.path.check'))}</button>
      <button id="setup-detect" class="secondary">${esc(t('setup.path.detect'))}</button>
    </div>
    <div id="setup-candidates"></div>
    <div id="setup-checks">${setupChecks(probe)}</div>

    <h3 class="section">${esc(t('setup.address'))}</h3>
    <div class="setup-row">
      <select id="setup-address">
        ${addresses
          .map(
            (address) =>
              '<option value="' + esc(address) + '"' + (address === state.setupAddress ? ' selected' : '') + '>' + esc(address) + '</option>'
          )
          .join('')}
      </select>
      <span class="note grow">${esc(t('setup.address.note'))}</span>
    </div>

    <div class="setup-row end">
      <button id="setup-apply" class="primary">${esc(t('setup.apply'))}</button>
      <span id="setup-next" class="note"></span>
    </div>

    ${region === 'cn' ? `
    <h3 class="section">${esc(t('setup.hosts'))}
      <span class="pill ${hosts.entries && hosts.entries.length ? 'ok' : 'warn'}">${
        hosts.entries && hosts.entries.length
          ? esc(t('setup.hosts.on', hosts.entries.length))
          : esc(t('setup.hosts.off'))
      }</span>
    </h3>
    <p class="note">${esc(t('setup.hosts.note'))}</p>
    <div class="setup-row">
      <button id="hosts-remove" class="secondary">${esc(t('setup.hosts.remove'))}</button>
      <span id="hosts-msg" class="note grow"></span>
    </div>
    <details class="domains">
      <summary>${esc(t('setup.hosts.domains'))} · ${(data.cn_domains || []).length}</summary>
      <ul class="mono">${(data.cn_domains || []).map((domain) => '<li>' + esc(domain) + '</li>').join('')}</ul>
    </details>
    ` : `
    <h3 class="section">${esc(t('setup.launch'))}</h3>
    <p class="note">${esc(t('setup.launch.note'))}</p>
    ${data.patch_present
      ? '<p class="note">' + esc(t('setup.patch.ready')) + '</p>'
      : '<p class="note warn-text">' + esc(t('setup.patch.missing')) + '</p>'}
    <div class="setup-row">
      <button id="setup-launch" class="secondary">${esc(t('setup.launch'))}</button>
      <span id="setup-launch-msg" class="note grow"></span>
    </div>
    `}

    <h3 class="section">${esc(t('setup.current'))}</h3>
    <table class="kv">
      <tr><th>${esc(t('setup.current.region'))}</th><td>${esc(setupRegionLabel(data.region))}</td></tr>
      <tr><th>Game.exe</th><td class="mono">${esc(data.game_path || '—')}</td></tr>
      <tr><th>-userdir</th><td class="mono">${esc(data.user_dir || '—')}</td></tr>
      <tr><th>args</th><td class="mono">${esc((data.arguments || []).join(' '))}</td></tr>
      <tr><th>${esc(t('setup.current.address'))}</th><td class="mono">${esc(data.public_address || '—')}</td></tr>
      <tr><th>${esc(t('setup.current.proxy'))}</th><td class="mono">${esc(
        data.proxy && data.proxy.enabled && data.proxy.set_all_proxy ? '127.0.0.1:' + data.proxy.port : 'off'
      )}</td></tr>
    </table>
  `;

  const pathInput = document.getElementById('setup-path');
  const checksBox = document.getElementById('setup-checks');

  async function probePath() {
    checksBox.innerHTML = '<p class="note">…</p>';
    try {
      const result = await api('/setup/probe', {
        method: 'POST',
        body: { path: pathInput.value, region: state.setupRegion }
      });
      checksBox.innerHTML = setupChecks(result.probe);
    } catch (error) {
      checksBox.innerHTML = '<p class="note">' + esc(error.message) + '</p>';
    }
  }

  Array.from(view.querySelectorAll('[data-region]')).forEach((button) => {
    button.addEventListener('click', () => {
      state.setupRegion = button.dataset.region;
      renderSetup().catch(reportError);
    });
  });

  document.getElementById('setup-check').addEventListener('click', probePath);
  pathInput.addEventListener('keydown', (event) => {
    if (event.key === 'Enter') probePath();
  });

  document.getElementById('setup-detect').addEventListener('click', async () => {
    const box = document.getElementById('setup-candidates');
    box.innerHTML = '<p class="note">' + esc(t('setup.path.detecting')) + '</p>';
    try {
      const result = await api('/setup/detect');
      const candidates = result.candidates || [];
      if (!candidates.length) {
        box.innerHTML = '';
        return;
      }
      box.innerHTML =
        '<p class="note">' + esc(t('setup.path.found', candidates.length)) + '</p><div class="setup-row">' +
        candidates
          .map((item) => '<button class="chip" data-candidate="' + esc(item) + '">' + esc(item) + '</button>')
          .join('') +
        '</div>';
      Array.from(box.querySelectorAll('[data-candidate]')).forEach((button) => {
        button.addEventListener('click', () => {
          pathInput.value = button.dataset.candidate;
          probePath();
        });
      });
    } catch (error) {
      box.innerHTML = '<p class="note">' + esc(error.message) + '</p>';
    }
  });

  document.getElementById('setup-address').addEventListener('change', (event) => {
    state.setupAddress = event.target.value;
  });

  const applyButton = document.getElementById('setup-apply');
  applyButton.addEventListener('click', async () => {
    // An empty box used to go straight to the server and come back as a bare "HTTP 400"; say what is
    // missing instead.
    if (!pathInput.value.trim()) {
      toast(t('setup.path.required'), true);
      pathInput.focus();
      return;
    }
    applyButton.disabled = true;
    const label = applyButton.textContent;
    applyButton.textContent = t('setup.applying');
    try {
      const result = await api('/setup/apply', {
        method: 'POST',
        body: {
          region: state.setupRegion,
          game_path: pathInput.value,
          public_address: state.setupAddress
        }
      });
      toast(t('setup.done'));
      state.setupRegion = result.region;
      await renderSetup();
      const next = document.getElementById('setup-next');
      if (next) {
        const steps = (result.next_steps || []).map((step) => esc(step)).join('<br>');
        // The wizard writes the hosts block as part of this same button. Report it only when something is
        // off: a success used to be a second button the operator had to find on their own.
        next.innerHTML = steps + setupHostsReport(result.hosts_sync, data);
      }
    } catch (error) {
      // invalid_path comes back with the full probe result; show it under the path box instead of
      // leaving the operator with an opaque status code.
      if (error.body && error.body.probe) {
        checksBox.innerHTML = setupChecks(error.body.probe);
      }
      reportError(error);
      applyButton.disabled = false;
      applyButton.textContent = label;
    }
  });

  const hostsRemove = document.getElementById('hosts-remove');
  if (hostsRemove) {
    hostsRemove.addEventListener('click', async () => {
      const message = document.getElementById('hosts-msg');
      message.textContent = '…';
      try {
        const result = await api('/setup/hosts', { method: 'POST', body: { action: 'remove' } });
        await renderSetup();
        const again = document.getElementById('hosts-msg');
        const text = setupHostsText(result.status, result, data);
        if (again) again.innerHTML = text;
        toast(text.replace(/<[^>]+>/g, ' '), result.status === 'needs_admin' || result.status === 'failed');
      } catch (error) {
        message.textContent = t('setup.hosts.failed', error.message);
      }
    });
  }

  // Global build only: "write config" stores a path, nothing more - the patch DLL is fetched by the loader
  // right before it injects, so the only way to witness that download is to start the client from here.
  const launchButton = document.getElementById('setup-launch');
  if (launchButton) {
    launchButton.addEventListener('click', async () => {
      const message = document.getElementById('setup-launch-msg');
      const label = launchButton.textContent;
      launchButton.disabled = true;
      launchButton.textContent = t('setup.launch.running');
      if (message) message.textContent = '';
      try {
        const result = await api('/setup/launch', { method: 'POST', body: {} });
        const detail = (result.messages || []).join(' ') || result.command;
        const text = result.success ? t('setup.launch.done', detail) : t('setup.launch.failed', detail);
        toast(text, !result.success);
        await renderSetup();
        const again = document.getElementById('setup-launch-msg');
        if (again) again.textContent = text;
      } catch (error) {
        if (message) message.textContent = t('setup.launch.failed', error.message);
        reportError(error);
      } finally {
        launchButton.disabled = false;
        launchButton.textContent = label;
      }
    });
  }
}

/// Turns a hosts_sync / setup/hosts answer into a one-line report. Success stays quiet (the pill above already
/// shows the entry count); only "needs administrator" and real failures are spelled out, because that is the
/// case where the operator has to do something by hand.
function setupHostsText(status, payload, data) {
  const path = (data && data.hosts && data.hosts.path) || 'hosts';
  switch (status) {
    case 'applied':
      return esc(t('setup.hosts.on', (payload.entries || []).length));
    case 'already':
      return esc(t('setup.hosts.already'));
    case 'removed':
      return esc(t('setup.hosts.removedOk'));
    case 'clear':
      return esc(t('setup.hosts.none'));
    case 'needs_admin':
      return '<span class="warn-text">' + esc(t('setup.hosts.needsAdmin', path)) + '</span>';
    default:
      return '<span class="warn-text">' + esc(t('setup.hosts.failed', (payload && payload.message) || status)) + '</span>';
  }
}

function setupHostsReport(sync, data) {
  if (!sync) return '';
  const text = setupHostsText(sync.status, sync, data);
  const quiet = sync.status === 'applied' || sync.status === 'already';
  return '<br>' + (quiet ? '<span class="note">' + text + '</span>' : text);
}

function formatDuration(seconds) {
  const total = Math.max(0, Math.floor(seconds || 0));
  const hours = Math.floor(total / 3600);
  const minutes = Math.floor((total % 3600) / 60);
  if (hours > 0) return t('dur.hm', hours, minutes);
  if (minutes > 0) return t('dur.ms', minutes, total % 60);
  return t('dur.s', total);
}

function formatTime(unixSeconds) {
  if (!unixSeconds) return '—';
  const date = new Date(unixSeconds * 1000);
  const pad = (value) => String(value).padStart(2, '0');
  return date.getFullYear() + '-' + pad(date.getMonth() + 1) + '-' + pad(date.getDate()) + ' ' + pad(date.getHours()) + ':' + pad(date.getMinutes());
}

function formatDate(unixSeconds) {
  if (!unixSeconds) return '—';
  return formatTime(unixSeconds).slice(0, 10);
}

function formatRange(startUnix, endUnix) {
  if (!startUnix && !endUnix) return '—';
  if (!endUnix) return formatDate(startUnix) + ' → ' + t('replay.longTerm');
  return formatDate(startUnix) + ' → ' + formatDate(endUnix);
}

function reportError(error) {
  if (error && error.message === 'unauthorized') return;
  var extra = error && error.body && error.body.code ? ' (' + error.body.code + ')' : '';
  toast(((error && error.message) || t('load.failed')) + extra, true);
}

async function route() {
  const hash = location.hash.replace(/^#\/?/, '');
  const parts = hash.split('/').filter(Boolean);
  try {
    if (parts[0] === 'setup') await renderSetup();
    else if (parts[0] === 'accounts') await renderAccounts(parts[1] ? Number(parts[1]) : undefined);
    else if (parts[0] === 'online') await renderOnline();
    else if (parts[0] === 'players') await renderPlayer(Number(parts[1] || 0));
    else if (parts[0] === 'replay') await renderReplay();
    else if (parts[0] === 'console') await renderConsole();
    else await renderOverview();
  } catch (error) {
    reportError(error);
    // A render that throws halfway leaves the previous page on screen, so a nav click looks like a no-op.
    // Say so in the page body as well as in the toast, and keep the failure in the console for a report.
    console.error(error);
    view.innerHTML =
      '<h2>' + esc(t('nav.' + (parts[0] || 'overview'))) + '</h2>' +
      '<p class="note warn-text">' + esc(error.message || String(error)) + '</p>';
  }
}

document.getElementById('login-form').addEventListener('submit', async (event) => {
  event.preventDefault();
  const token = document.getElementById('login-token').value.trim();
  if (!token) return;
  state.token = token;
  try {
    await api('/health');
  } catch (error) {
    state.token = '';
    showLogin(t('login.invalid'));
    return;
  }
  localStorage.setItem(TOKEN_KEY, token);
  hideLogin();
  maybeShowNotice();
  state.currencies = null;
  state.commands = null;
  refreshStatus();
  route();
});

window.addEventListener('hashchange', route);

(async function boot() {
  // ?token=… is a one-shot convenience link: adopt it, then strip it from the address bar.
  const params = new URLSearchParams(location.search);
  const linkToken = params.get('token');
  if (linkToken) {
    state.token = linkToken.trim();
    localStorage.setItem(TOKEN_KEY, state.token);
    params.delete('token');
    const query = params.toString();
    history.replaceState(null, '', location.pathname + (query ? '?' + query : '') + location.hash);
  }

  applyShell();

  if (!state.token) {
    showLogin('');
    return;
  }
  try {
    await api('/health');
  } catch (error) {
    if (error.message !== 'unauthorized') showLogin(t('login.connect', error.message));
    return;
  }
  hideLogin();
  maybeShowNotice();
  refreshStatus();
  // Nothing configured yet means this is a fresh install: land on the wizard, not an empty overview.
  try {
    const setup = await api('/setup');
    if (!setup.probe || !setup.probe.valid) {
      if (!location.hash) location.hash = '#/setup';
      state.setupRegion = setup.region || 'global';
    }
  } catch (error) {
    // A failing probe must not block the panel; the operator can still open Setup manually.
  }
  route();
})();
