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
    'nav.replay': '活动复刻',
    'nav.console': '控制台',
    'status.connecting': '连接中…',
    'status.offline': '未连接',
    'status.running': 'v{0} · 运行 {1}',
    'status.error': '接口错误',
    'login.title': '需要访问令牌',
    'login.hint': '令牌在服务端 Config\\admin-token.txt。以管理员运行时剪贴板不与浏览器共享，双击 Config\\admin-panel.url 即可带令牌打开面板；普通权限启动会自动复制令牌并用浏览器打开。',
    'update.title': '更新',
    'update.current': '当前版本',
    'update.idle': '未检查',
    'update.check': '检查更新',
    'update.apply': '立即更新',
    'update.checking': '正在检查…',
    'update.upToDate': '已是最新版本（{0}）',
    'update.available': '发现新版本 {0}',
    'update.applying': '正在下载并切换到新版本，面板会短暂断开…',
    'update.failed': '更新失败：{0}',
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
    'accounts.th.ban': '封禁',
    'accounts.th.perms': '权限',
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
    'console.aliases': '别名',
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
    'nav.replay': 'Era replay',
    'nav.console': 'Console',
    'status.connecting': 'Connecting…',
    'status.offline': 'Offline',
    'status.running': 'v{0} · up {1}',
    'status.error': 'API error',
    'login.title': 'Access token required',
    'login.hint': 'The token lives in Config\\admin-token.txt on the server. When you run as Administrator the clipboard is not shared with the browser, so double-click Config\\admin-panel.url to open the panel with the token; a normal launch copies it and opens the browser for you.',
    'update.title': 'Update',
    'update.current': 'Current version',
    'update.idle': 'Not checked',
    'update.check': 'Check for updates',
    'update.apply': 'Update now',
    'update.checking': 'Checking…',
    'update.upToDate': 'Up to date ({0})',
    'update.available': 'Version {0} is available',
    'update.applying': 'Downloading and switching to the new build; the panel will reconnect shortly…',
    'update.failed': 'Update failed: {0}',
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
    'accounts.th.ban': 'Ban',
    'accounts.th.perms': 'Permissions',
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
    'console.aliases': 'Aliases',
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
    'nav.replay': '活動復刻',
    'nav.console': '控制台',
    'status.connecting': '連線中…',
    'status.offline': '未連線',
    'status.running': 'v{0} · 執行 {1}',
    'status.error': '介面錯誤',
    'login.title': '需要存取權杖',
    'login.hint': '權杖在伺服端 Config\\admin-token.txt。以系統管理員身分執行時剪貼簿不會與瀏覽器共用，雙擊 Config\\admin-panel.url 就能帶權杖開啟面板；一般權限啟動會自動複製權杖並開啟瀏覽器。',
    'update.title': '更新',
    'update.current': '目前版本',
    'update.idle': '未檢查',
    'update.check': '檢查更新',
    'update.apply': '立即更新',
    'update.checking': '正在檢查…',
    'update.upToDate': '已是最新版本（{0}）',
    'update.available': '發現新版本 {0}',
    'update.applying': '正在下載並切換到新版本，面板會短暫中斷…',
    'update.failed': '更新失敗：{0}',
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
    'accounts.th.ban': '封禁',
    'accounts.th.perms': '權限',
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
    'console.aliases': '別名',
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
    'nav.replay': 'イベント復刻',
    'nav.console': 'コンソール',
    'status.connecting': '接続中…',
    'status.offline': '未接続',
    'status.running': 'v{0} · 稼働 {1}',
    'status.error': 'API エラー',
    'login.title': 'アクセストークンが必要です',
    'login.hint': 'トークンはサーバー側の Config\\admin-token.txt にあります。管理者として実行するとクリップボードはブラウザーと共有されないため、Config\\admin-panel.url をダブルクリックしてトークン付きで開いてください。通常起動では自動でコピーされブラウザーが開きます。',
    'update.title': 'アップデート',
    'update.current': '現在のバージョン',
    'update.idle': '未確認',
    'update.check': '更新を確認',
    'update.apply': '今すぐ更新',
    'update.checking': '確認中…',
    'update.upToDate': '最新です（{0}）',
    'update.available': '新しいバージョン {0} があります',
    'update.applying': '新しいビルドをダウンロードして切り替えています。パネルはまもなく再接続します…',
    'update.failed': '更新に失敗しました：{0}',
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
    'accounts.th.ban': 'BAN',
    'accounts.th.perms': '権限',
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
    'console.aliases': '別名',
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
    'nav.replay': '활동 복각',
    'nav.console': '콘솔',
    'status.connecting': '연결 중…',
    'status.offline': '연결 안 됨',
    'status.running': 'v{0} · {1} 실행 중',
    'status.error': 'API 오류',
    'login.title': '액세스 토큰 필요',
    'login.hint': '토큰은 서버의 Config\\admin-token.txt 에 있습니다. 관리자 권한으로 실행하면 클립보드가 브라우저와 공유되지 않으므로 Config\\admin-panel.url 을 두 번 클릭해 토큰과 함께 여십시오. 일반 권한으로 실행하면 자동으로 복사되고 브라우저가 열립니다.',
    'update.title': '업데이트',
    'update.current': '현재 버전',
    'update.idle': '확인 안 함',
    'update.check': '업데이트 확인',
    'update.apply': '지금 업데이트',
    'update.checking': '확인 중…',
    'update.upToDate': '최신 버전입니다 ({0})',
    'update.available': '새 버전 {0} 사용 가능',
    'update.applying': '새 빌드를 내려받아 전환하는 중입니다. 패널이 곧 다시 연결됩니다…',
    'update.failed': '업데이트 실패: {0}',
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
    'accounts.th.ban': '차단',
    'accounts.th.perms': '권한',
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
    'console.aliases': '별칭',
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
    'nav.replay': 'Era-Replay',
    'nav.console': 'Konsole',
    'status.connecting': 'Verbinde…',
    'status.offline': 'Nicht verbunden',
    'status.running': 'v{0} · läuft {1}',
    'status.error': 'API-Fehler',
    'login.title': 'Zugriffstoken erforderlich',
    'login.hint': 'Das Token liegt serverseitig in Config\\admin-token.txt. Als Administrator wird die Zwischenablage nicht mit dem Browser geteilt: Doppelklicke Config\\admin-panel.url, um das Panel mit Token zu öffnen; bei normalem Start wird es kopiert und der Browser geöffnet.',
    'update.title': 'Aktualisierung',
    'update.current': 'Aktuelle Version',
    'update.idle': 'Nicht geprüft',
    'update.check': 'Nach Updates suchen',
    'update.apply': 'Jetzt aktualisieren',
    'update.checking': 'Wird geprüft…',
    'update.upToDate': 'Aktuell ({0})',
    'update.available': 'Version {0} ist verfügbar',
    'update.applying': 'Neuer Build wird geladen und gewechselt; das Panel verbindet sich gleich wieder…',
    'update.failed': 'Aktualisierung fehlgeschlagen: {0}',
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
    'accounts.th.ban': 'Sperre',
    'accounts.th.perms': 'Rechte',
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
    'console.aliases': 'Aliasse',
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
    'nav.replay': 'Rejeu d’ère',
    'nav.console': 'Console',
    'status.connecting': 'Connexion…',
    'status.offline': 'Hors ligne',
    'status.running': 'v{0} · actif depuis {1}',
    'status.error': 'Erreur d’API',
    'login.title': 'Jeton d’accès requis',
    'login.hint': 'Le jeton se trouve dans Config\\admin-token.txt côté serveur. Lancé en administrateur, le presse-papiers n’est pas partagé avec le navigateur : double-cliquez sur Config\\admin-panel.url pour ouvrir le panneau avec le jeton ; en lancement normal il est copié et le navigateur s’ouvre.',
    'update.title': 'Mise à jour',
    'update.current': 'Version actuelle',
    'update.idle': 'Non vérifié',
    'update.check': 'Vérifier les mises à jour',
    'update.apply': 'Mettre à jour',
    'update.checking': 'Vérification…',
    'update.upToDate': 'À jour ({0})',
    'update.available': 'La version {0} est disponible',
    'update.applying': 'Téléchargement et bascule vers la nouvelle version, le panneau se reconnecte dans un instant…',
    'update.failed': 'Échec de la mise à jour : {0}',
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
    'accounts.th.ban': 'Bannissement',
    'accounts.th.perms': 'Droits',
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
    'console.aliases': 'Alias',
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
    'nav.replay': 'Repetición de era',
    'nav.console': 'Consola',
    'status.connecting': 'Conectando…',
    'status.offline': 'Sin conexión',
    'status.running': 'v{0} · activo {1}',
    'status.error': 'Error de API',
    'login.title': 'Se requiere token de acceso',
    'login.hint': 'El token está en Config\\admin-token.txt en el servidor. Si lo ejecutas como administrador, el portapapeles no se comparte con el navegador: haz doble clic en Config\\admin-panel.url para abrir el panel con el token; con permisos normales se copia y se abre el navegador.',
    'update.title': 'Actualización',
    'update.current': 'Versión actual',
    'update.idle': 'Sin comprobar',
    'update.check': 'Buscar actualizaciones',
    'update.apply': 'Actualizar ahora',
    'update.checking': 'Comprobando…',
    'update.upToDate': 'Ya está al día ({0})',
    'update.available': 'La versión {0} está disponible',
    'update.applying': 'Descargando y cambiando a la nueva compilación; el panel se reconectará en breve…',
    'update.failed': 'Error de actualización: {0}',
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
    'accounts.th.ban': 'Bloqueo',
    'accounts.th.perms': 'Permisos',
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
    'console.aliases': 'Alias',
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
    'nav.replay': 'ย้อนยุคกิจกรรม',
    'nav.console': 'คอนโซล',
    'status.connecting': 'กำลังเชื่อมต่อ…',
    'status.offline': 'ไม่ได้เชื่อมต่อ',
    'status.running': 'v{0} · ทำงาน {1}',
    'status.error': 'API ผิดพลาด',
    'login.title': 'ต้องใช้โทเคนเข้าถึง',
    'login.hint': 'โทเคนอยู่ใน Config\\admin-token.txt ฝั่งเซิร์ฟเวอร์ เมื่อรันในฐานะผู้ดูแลระบบ คลิปบอร์ดจะไม่ถูกแชร์กับเบราว์เซอร์ ให้ดับเบิลคลิก Config\\admin-panel.url เพื่อเปิดแผงพร้อมโทเคน หากรันแบบปกติจะคัดลอกให้และเปิดเบราว์เซอร์',
    'update.title': 'อัปเดต',
    'update.current': 'เวอร์ชันปัจจุบัน',
    'update.idle': 'ยังไม่ตรวจสอบ',
    'update.check': 'ตรวจหาการอัปเดต',
    'update.apply': 'อัปเดตเลย',
    'update.checking': 'กำลังตรวจสอบ…',
    'update.upToDate': 'เป็นเวอร์ชันล่าสุดแล้ว ({0})',
    'update.available': 'มีเวอร์ชันใหม่ {0}',
    'update.applying': 'กำลังดาวน์โหลดและสลับไปยังบิลด์ใหม่ แผงควบคุมจะเชื่อมต่อใหม่ในอีกสักครู่…',
    'update.failed': 'อัปเดตล้มเหลว: {0}',
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
    'accounts.th.ban': 'แบน',
    'accounts.th.perms': 'สิทธิ์',
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
    'console.aliases': 'ชื่อเรียกอื่น',
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
    'nav.replay': 'Ulang era',
    'nav.console': 'Konsol',
    'status.connecting': 'Menghubungkan…',
    'status.offline': 'Tidak terhubung',
    'status.running': 'v{0} · aktif {1}',
    'status.error': 'Kesalahan API',
    'login.title': 'Token akses diperlukan',
    'login.hint': 'Token ada di Config\\admin-token.txt di sisi server. Saat dijalankan sebagai Administrator, clipboard tidak dibagikan ke browser, jadi klik dua kali Config\\admin-panel.url untuk membuka panel dengan token; peluncuran normal menyalinnya dan membuka browser.',
    'update.title': 'Pembaruan',
    'update.current': 'Versi saat ini',
    'update.idle': 'Belum dicek',
    'update.check': 'Periksa pembaruan',
    'update.apply': 'Perbarui sekarang',
    'update.checking': 'Memeriksa…',
    'update.upToDate': 'Sudah versi terbaru ({0})',
    'update.available': 'Versi {0} tersedia',
    'update.applying': 'Mengunduh dan beralih ke build baru; panel akan tersambung lagi sebentar…',
    'update.failed': 'Pembaruan gagal: {0}',
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
    'accounts.th.ban': 'Blokir',
    'accounts.th.perms': 'Izin',
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
    'console.aliases': 'Alias',
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
    "setup.hosts.dllmode": "DLL 注入模式不使用 hosts：写入配置时会清空 MikuSB 写的 hosts 段。",
    "setup.launch.dll.note": "点下面的按钮启动游戏：面板把国服补丁 DLL 注入游戏进程，由 DLL 把 SDK 请求引到本机。",
    "setup.launch.hosts.note": "点下面的按钮启动游戏：面板先把 7 个 SDK 域名写进 hosts，客户端自己连到本机。",
    "setup.region.cn.note": "面板要把国服的 SDK 请求引到本机；用 hosts 还是 DLL 注入，在第 1 步下面选。",
    "setup.region.global": "国际服",
    "setup.region.none": "未选择",
    "setup.region.pick": "请先选择服务器区域：国服和国际服的初始化方式完全不同。",
    "setup.region.global.note": "沿用上游 Loader：注入 MikuSB-Patch.dll 并走本地 SOCKS5，面板只帮你代填路径与参数。",
    "setup.region.cn.launcher": "国服仅适配官服旧版启动器 {0}，新版启动器无法登录。旧版启动器下载：",
    "setup.region.cn.download": "点此下载",
    "setup.path": "2 客户端路径",
    "setup.path.placeholder": "例如 D:\\Games\\Snow，或 …\\data\\game\\Game\\Binaries\\Win64\\Game.exe",
    "setup.path.check": "检查路径",
    "setup.path.detect": "自动探测",
    "setup.path.detecting": "正在扫描磁盘…",
    "setup.path.found": "找到 {0} 个候选",
    "setup.check.input": "客户端路径",
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
    "setup.hosts.elevated": "已以管理员权限运行，可直接写入 hosts。",
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
    "setup.path.hint": "还没检查过这个路径：填好后点「检查路径」，或直接点「自动探测」。","setup.path.required": "先填客户端路径，或点「自动探测」让面板自己找。"
  },
  "zh_TW": {
    "nav.setup": "初始化",
    "setup.title": "初始化精靈",
    "setup.lead": "三步完成：選伺服器區域 → 填客戶端路徑 → 寫入設定。面板會先讀出現有設定，只改你在這裡確認的項目。",
    "setup.region": "1 伺服器區域",
    "setup.region.cn": "國服（jinshan）",
    "setup.hosts.dllmode": "DLL 注入模式不使用 hosts：寫入設定時會清空 MikuSB 寫的 hosts 段落。",
    "setup.launch.dll.note": "點下面的按鈕啟動遊戲：面板把國服補丁 DLL 注入遊戲進程，由 DLL 把 SDK 請求導到本機。",
    "setup.launch.hosts.note": "點下面的按鈕啟動遊戲：面板先把 7 個 SDK 網域寫進 hosts，用戶端自行連到本機。",
    "setup.region.cn.note": "面板要把國服的 SDK 請求導到本機；用 hosts 還是 DLL 注入，在第 1 步下面選。",
    "setup.region.global": "國際服",
    "setup.region.none": "未選擇",
    "setup.region.pick": "請先選擇伺服器區域：國服和國際服的初始化方式完全不同。",
    "setup.region.global.note": "沿用上游 Loader：注入 MikuSB-Patch.dll 並走本機 SOCKS5，面板只幫你代填路徑與參數。",
    "setup.region.cn.launcher": "國服僅適配官服舊版啟動器 {0}，新版啟動器無法登入。舊版啟動器下載：",
    "setup.region.cn.download": "點此下載",
    "setup.path": "2 客戶端路徑",
    "setup.path.placeholder": "例如 D:\\Games\\Snow，或 …\\data\\game\\Game\\Binaries\\Win64\\Game.exe",
    "setup.path.check": "檢查路徑",
    "setup.path.detect": "自動偵測",
    "setup.path.detecting": "正在掃描磁碟…",
    "setup.path.found": "找到 {0} 個候選",
    "setup.check.input": "用戶端路徑",
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
    "setup.hosts.elevated": "已以系統管理員權限執行，可直接寫入 hosts。",
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
    "setup.path.hint": "還沒檢查過這個路徑：填好後點「檢查路徑」，或直接點「自動偵測」。","setup.path.required": "先填客戶端路徑，或點「自動偵測」讓面板自己找。"
  },
  "ja_JP": {
    "nav.setup": "初期設定",
    "setup.title": "初期設定ウィザード",
    "setup.lead": "3 ステップ：サーバー地域 → クライアントのパス → 設定の書き込み。既存の設定を読み出し、確認した項目だけを書き換えます。",
    "setup.region": "1 サーバー地域",
    "setup.region.cn": "中国版（jinshan）",
    "setup.hosts.dllmode": "DLL 注入モードでは hosts を使いません。設定を書き込むと MikuSB の hosts 行は消去されます。",
    "setup.launch.dll.note": "下のボタンで起動します。パネルが CN パッチ DLL をゲームに注入し、その DLL が SDK 通信をこの PC に振り向けます。",
    "setup.launch.hosts.note": "下のボタンで起動します。パネルが 7 つの SDK ドメインを hosts に書き込み、クライアントが自分からこの PC に接続します。",
    "setup.region.cn.note": "パネルは CN の SDK 通信をこの PC に向ける必要があります。hosts か DLL 注入かは手順 1 で選びます。",
    "setup.region.global": "グローバル版",
    "setup.region.none": "未選択",
    "setup.region.pick": "先にサーバー地域を選択してください。中国版とグローバル版では初期設定の方法がまったく異なります。",
    "setup.region.global.note": "上流の Loader 方式：MikuSB-Patch.dll を注入しローカル SOCKS5 を使用。パネルはパスと引数の入力だけ補助します。",
    "setup.region.cn.launcher": "CN 版は公式の旧ランチャー {0} のみ対応しています。旧ランチャーのダウンロード：",
    "setup.region.cn.download": "ダウンロード",
    "setup.path": "2 クライアントのパス",
    "setup.path.placeholder": "例 D:\\Games\\Snow、または …\\data\\game\\Game\\Binaries\\Win64\\Game.exe",
    "setup.path.check": "パスを確認",
    "setup.path.detect": "自動検出",
    "setup.path.detecting": "ディスクを走査中…",
    "setup.path.found": "{0} 件の候補",
    "setup.check.input": "クライアントのパス",
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
    "setup.hosts.elevated": "管理者権限で実行中のため、hosts を直接書き込めます。",
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
    "setup.path.hint": "このパスはまだ確認していません。「パスを確認」か「自動検出」を使ってください。","setup.path.required": "クライアントのパスを入力するか、「自動検出」で探してください。"
  },
  "ko_KR": {
    "nav.setup": "초기 설정",
    "setup.title": "초기 설정 마법사",
    "setup.lead": "세 단계: 서버 지역 선택 → 클라이언트 경로 입력 → 설정 저장. 기존 설정을 먼저 읽고 확인한 항목만 바꿉니다.",
    "setup.region": "1 서버 지역",
    "setup.region.cn": "중국 서버(jinshan)",
    "setup.hosts.dllmode": "DLL 주입 모드에서는 hosts 를 쓰지 않습니다. 설정을 저장하면 MikuSB 가 쓴 hosts 줄이 지워집니다.",
    "setup.launch.dll.note": "아래 버튼으로 실행합니다. 패널이 CN 패치 DLL 을 게임에 주입하고, 그 DLL 이 SDK 요청을 이 PC 로 돌립니다.",
    "setup.launch.hosts.note": "아래 버튼으로 실행합니다. 패널이 7개 SDK 도메인을 hosts 에 쓰고, 클라이언트가 스스로 이 PC 로 접속합니다.",
    "setup.region.cn.note": "패널이 CN SDK 요청을 이 PC 로 돌려야 합니다. hosts 인지 DLL 주입인지는 1단계에서 고릅니다.",
    "setup.region.global": "글로벌 서버",
    "setup.region.none": "선택 안 함",
    "setup.region.pick": "먼저 서버 지역을 선택하세요. 중국 서버와 글로벌 서버는 초기 설정 방식이 완전히 다릅니다.",
    "setup.region.global.note": "업스트림 Loader 방식: MikuSB-Patch.dll 주입 + 로컬 SOCKS5. 패널은 경로와 인자만 채워 줍니다.",
    "setup.region.cn.launcher": "CN 서버는 공식 구버전 런처 {0} 에만 대응합니다. 구버전 런처 다운로드:",
    "setup.region.cn.download": "다운로드",
    "setup.path": "2 클라이언트 경로",
    "setup.path.placeholder": "예: D:\\Games\\Snow 또는 …\\data\\game\\Game\\Binaries\\Win64\\Game.exe",
    "setup.path.check": "경로 확인",
    "setup.path.detect": "자동 탐지",
    "setup.path.detecting": "디스크 검색 중…",
    "setup.path.found": "후보 {0}개",
    "setup.check.input": "클라이언트 경로",
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
    "setup.hosts.elevated": "관리자 권한으로 실행 중이므로 hosts 를 바로 기록할 수 있습니다.",
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
    "setup.path.hint": "아직 이 경로를 확인하지 않았습니다. 위의 확인 또는 자동 탐지를 사용하세요.","setup.path.required": "클라이언트 경로를 입력하거나 \"자동 탐지\"를 눌러 주세요."
  },
  "en_US": {
    "nav.setup": "Setup",
    "setup.title": "Setup wizard",
    "setup.lead": "Three steps: pick the server region, point at the client, write the config. The panel reads your current config first and only rewrites what you confirm here.",
    "setup.region": "1 Server region",
    "setup.region.cn": "China (jinshan)",
    "setup.hosts.dllmode": "DLL injection does not use hosts: writing the config clears the MikuSB block from the hosts file.",
    "setup.launch.dll.note": "Press the button below: the panel injects the CN patch DLL into the game, and the DLL points the SDK calls at this machine.",
    "setup.launch.hosts.note": "Press the button below: the panel writes the seven SDK domains into hosts, and the client connects here by itself.",
    "setup.region.cn.note": "The panel has to point the CN SDK traffic at this machine; pick hosts or DLL injection in step 1.",
    "setup.region.global": "Global",
    "setup.region.none": "Not selected",
    "setup.region.pick": "Pick a server region first: the CN and global builds are set up in completely different ways.",
    "setup.region.global.note": "Upstream loader path: inject MikuSB-Patch.dll and use the local SOCKS5 proxy. The panel just pre-fills the paths and arguments.",
    "setup.region.cn.launcher": "The CN build only works with the old official launcher {0}. Download it here:",
    "setup.region.cn.download": "Download",
    "setup.path": "2 Client path",
    "setup.path.placeholder": "e.g. D:\\Games\\Snow, or …\\data\\game\\Game\\Binaries\\Win64\\Game.exe",
    "setup.path.check": "Check path",
    "setup.path.detect": "Auto-detect",
    "setup.path.detecting": "Scanning drives…",
    "setup.path.found": "{0} candidate(s) found",
    "setup.check.input": "Client path",
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
    "setup.hosts.elevated": "Running as administrator, so hosts can be written directly.",
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
    "setup.path.hint": "This path has not been checked yet; use the check or detect button above.","setup.path.required": "Enter the client path, or press \"Detect\" to let the panel find it."
  },
  "fr_FR": {
    "nav.setup": "Installation",
    "setup.title": "Assistant d’installation",
    "setup.lead": "Trois étapes : région du serveur, chemin du client, écriture de la configuration. Le panneau lit d’abord la configuration existante et ne réécrit que ce que vous validez.",
    "setup.region": "1 Région du serveur",
    "setup.region.cn": "Chine (jinshan)",
    "setup.hosts.dllmode": "L'injection DLL n'utilise pas hosts : écrire la configuration efface le bloc MikuSB du fichier hosts.",
    "setup.launch.dll.note": "Cliquez sur le bouton ci-dessous : le panneau injecte la DLL de patch CN dans le jeu, et la DLL renvoie les appels SDK vers cette machine.",
    "setup.launch.hosts.note": "Cliquez sur le bouton ci-dessous : le panneau écrit les sept domaines SDK dans hosts, et le client se connecte ici de lui-même.",
    "setup.region.cn.note": "Le panneau doit diriger le trafic SDK CN vers cette machine ; choisissez hosts ou injection DLL à l'étape 1.",
    "setup.region.global": "International",
    "setup.region.none": "Non sélectionné",
    "setup.region.pick": "Choisissez d’abord une région de serveur : les versions CN et internationale ne se configurent pas du tout de la même façon.",
    "setup.region.global.note": "Méthode du loader d’origine : injection de MikuSB-Patch.dll et proxy SOCKS5 local. Le panneau ne fait que pré-remplir les chemins.",
    "setup.region.cn.launcher": "La version CN ne fonctionne qu'avec l'ancien lanceur officiel {0}. Téléchargement :",
    "setup.region.cn.download": "Télécharger",
    "setup.path": "2 Chemin du client",
    "setup.path.placeholder": "ex. D:\\Games\\Snow, ou …\\data\\game\\Game\\Binaries\\Win64\\Game.exe",
    "setup.path.check": "Vérifier le chemin",
    "setup.path.detect": "Détection auto",
    "setup.path.detecting": "Analyse des disques…",
    "setup.path.found": "{0} candidat(s)",
    "setup.check.input": "Chemin du client",
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
    "setup.hosts.elevated": "Exécuté en administrateur : hosts peut être écrit directement.",
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
    "setup.path.hint": "Ce chemin n'a pas encore été vérifié : utilisez le bouton de vérification ou de détection ci-dessus.","setup.path.required": "Saisissez le chemin du client, ou utilisez « Détecter »."
  },
  "es_ES": {
    "nav.setup": "Configuración",
    "setup.title": "Asistente de configuración",
    "setup.lead": "Tres pasos: región del servidor, ruta del cliente y escritura de la configuración. El panel lee primero tu configuración y solo reescribe lo que confirmes.",
    "setup.region": "1 Región del servidor",
    "setup.region.cn": "China (jinshan)",
    "setup.hosts.dllmode": "La inyección de DLL no usa hosts: al escribir la configuración se borra el bloque de MikuSB del archivo hosts.",
    "setup.launch.dll.note": "Pulsa el botón de abajo: el panel inyecta la DLL de parche CN en el juego y la DLL redirige las llamadas SDK a este equipo.",
    "setup.launch.hosts.note": "Pulsa el botón de abajo: el panel escribe los siete dominios SDK en hosts y el cliente se conecta aquí por su cuenta.",
    "setup.region.cn.note": "El panel debe dirigir el tráfico SDK de CN a este equipo; elige hosts o inyección de DLL en el paso 1.",
    "setup.region.global": "Internacional",
    "setup.region.none": "Sin seleccionar",
    "setup.region.pick": "Primero elige la región del servidor: las versiones CN e internacional se configuran de forma totalmente distinta.",
    "setup.region.global.note": "Ruta del loader original: inyecta MikuSB-Patch.dll y usa el proxy SOCKS5 local. El panel solo rellena las rutas.",
    "setup.region.cn.launcher": "La versión CN solo funciona con el lanzador oficial antiguo {0}. Descárgalo aquí:",
    "setup.region.cn.download": "Descargar",
    "setup.path": "2 Ruta del cliente",
    "setup.path.placeholder": "p. ej. D:\\Games\\Snow, o …\\data\\game\\Game\\Binaries\\Win64\\Game.exe",
    "setup.path.check": "Comprobar ruta",
    "setup.path.detect": "Detección automática",
    "setup.path.detecting": "Analizando discos…",
    "setup.path.found": "{0} candidato(s)",
    "setup.check.input": "Ruta del cliente",
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
    "setup.hosts.elevated": "Se ejecuta como administrateur : hosts puede escribirse directamente.",
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
    "setup.path.hint": "Esta ruta aún no se ha comprobado; usa el botón de comprobar o de detectar.","setup.path.required": "Introduce la ruta del cliente o pulsa «Detectar»."
  },
  "de_DE": {
    "nav.setup": "Einrichtung",
    "setup.title": "Einrichtungsassistent",
    "setup.lead": "Drei Schritte: Serverregion, Client-Pfad, Konfiguration schreiben. Das Panel liest die bestehende Konfiguration und ändert nur, was du hier bestätigst.",
    "setup.region": "1 Serverregion",
    "setup.region.cn": "China (jinshan)",
    "setup.hosts.dllmode": "DLL-Injektion nutzt kein hosts: beim Schreiben der Konfiguration wird der MikuSB-Block aus der hosts-Datei entfernt.",
    "setup.launch.dll.note": "Knopf unten drücken: das Panel injiziert die CN-Patch-DLL ins Spiel, und die DLL leitet die SDK-Aufrufe auf diesen Rechner.",
    "setup.launch.hosts.note": "Knopf unten drücken: das Panel schreibt die sieben SDK-Domains in hosts, und der Client verbindet sich selbst hierher.",
    "setup.region.cn.note": "Das Panel muss den CN-SDK-Verkehr auf diesen Rechner lenken; hosts oder DLL-Injektion wird in Schritt 1 gewählt.",
    "setup.region.global": "International",
    "setup.region.none": "Nicht ausgewählt",
    "setup.region.pick": "Wähle zuerst die Serverregion: CN- und internationale Version werden völlig unterschiedlich eingerichtet.",
    "setup.region.global.note": "Upstream-Loader: MikuSB-Patch.dll injizieren und lokalen SOCKS5 nutzen. Das Panel füllt nur Pfade und Argumente vor.",
    "setup.region.cn.launcher": "Die CN-Version funktioniert nur mit dem alten offiziellen Launcher {0}. Download:",
    "setup.region.cn.download": "Herunterladen",
    "setup.path": "2 Client-Pfad",
    "setup.path.placeholder": "z. B. D:\\Games\\Snow oder …\\data\\game\\Game\\Binaries\\Win64\\Game.exe",
    "setup.path.check": "Pfad prüfen",
    "setup.path.detect": "Automatisch suchen",
    "setup.path.detecting": "Laufwerke werden durchsucht…",
    "setup.path.found": "{0} Kandidat(en)",
    "setup.check.input": "Client-Pfad",
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
    "setup.hosts.elevated": "Läuft als Administrator: hosts kann direkt geschrieben werden.",
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
    "setup.path.hint": "Dieser Pfad wurde noch nicht geprüft; nutze oben „Prüfen“ oder „Erkennen“.","setup.path.required": "Client-Pfad eingeben oder „Erkennen“ drücken."
  },
  "th_TH": {
    "nav.setup": "ตั้งค่าเริ่มต้น",
    "setup.title": "ตัวช่วยตั้งค่าเริ่มต้น",
    "setup.lead": "สามขั้นตอน: เลือกภูมิภาคเซิร์ฟเวอร์ → ระบุที่อยู่ไคลเอนต์ → บันทึกการตั้งค่า แผงจะอ่านค่าที่มีอยู่ก่อนและแก้เฉพาะรายการที่คุณยืนยัน",
    "setup.region": "1 ภูมิภาคเซิร์ฟเวอร์",
    "setup.region.cn": "จีน (jinshan)",
    "setup.hosts.dllmode": "โหมดฉีด DLL ไม่ใช้ hosts: เมื่อเขียนค่าตั้ง ระบบจะลบบล็อก MikuSB ในไฟล์ hosts",
    "setup.launch.dll.note": "กดปุ่มด้านล่าง: แผงจะฉีด DLL แพตช์ CN เข้าไปในเกม และ DLL จะพาการเรียก SDK มาที่เครื่องนี้",
    "setup.launch.hosts.note": "กดปุ่มด้านล่าง: แผงจะเขียน 7 โดเมน SDK ลง hosts แล้วไคลเอนต์จะเชื่อมต่อมาที่เครื่องนี้เอง",
    "setup.region.cn.note": "แผงต้องพาทราฟฟิก SDK ของ CN มาที่เครื่องนี้ จะใช้ hosts หรือฉีด DLL เลือกในขั้นตอนที่ 1",
    "setup.region.global": "สากล",
    "setup.region.none": "ยังไม่ได้เลือก",
    "setup.region.pick": "เลือกภูมิภาคเซิร์ฟเวอร์ก่อน: เวอร์ชัน CN และสากลมีการตั้งค่าเริ่มต้นต่างกันโดยสิ้นเชิง",
    "setup.region.global.note": "ใช้วิธี Loader เดิม: ฉีด MikuSB-Patch.dll และใช้ SOCKS5 ในเครื่อง แผงเพียงกรอกพาธและพารามิเตอร์ให้",
    "setup.region.cn.launcher": "เวอร์ชัน CN ใช้ได้กับตัวเรียกเกมรุ่นเก่าของทางการ {0} เท่านั้น ดาวน์โหลด:",
    "setup.region.cn.download": "ดาวน์โหลด",
    "setup.path": "2 ที่อยู่ไคลเอนต์",
    "setup.path.placeholder": "เช่น D:\\Games\\Snow หรือ …\\data\\game\\Game\\Binaries\\Win64\\Game.exe",
    "setup.path.check": "ตรวจสอบพาธ",
    "setup.path.detect": "ค้นหาอัตโนมัติ",
    "setup.path.detecting": "กำลังสแกนดิสก์…",
    "setup.path.found": "พบ {0} รายการ",
    "setup.check.input": "เส้นทางไคลเอนต์",
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
    "setup.hosts.elevated": "รันด้วยสิทธิ์ผู้ดูแลระบบ จึงเขียน hosts ได้โดยตรง",
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
    "setup.path.hint": "ยังไม่ได้ตรวจสอบพาธนี้ ใช้ปุ่มตรวจสอบหรือตรวจหาอัตโนมัติด้านบน","setup.path.required": "กรอกพาธของไคลเอนต์ หรือกด \"ตรวจหาอัตโนมัติ\""
  },
  "id_ID": {
    "nav.setup": "Penyiapan",
    "setup.title": "Panduan penyiapan",
    "setup.lead": "Tiga langkah: pilih wilayah server, isi lokasi klien, tulis konfigurasi. Panel membaca konfigurasi yang ada lebih dulu dan hanya mengubah yang Anda konfirmasi.",
    "setup.region": "1 Wilayah server",
    "setup.region.cn": "Tiongkok (jinshan)",
    "setup.hosts.dllmode": "Mode suntik DLL tidak memakai hosts: saat konfigurasi ditulis, blok MikuSB di berkas hosts dihapus.",
    "setup.launch.dll.note": "Tekan tombol di bawah: panel menyuntikkan DLL patch CN ke dalam game, dan DLL mengarahkan panggilan SDK ke mesin ini.",
    "setup.launch.hosts.note": "Tekan tombol di bawah: panel menulis tujuh domain SDK ke hosts, dan klien tersambung ke sini dengan sendirinya.",
    "setup.region.cn.note": "Panel harus mengarahkan lalu lintas SDK CN ke mesin ini; pilih hosts atau suntik DLL di langkah 1.",
    "setup.region.global": "Internasional",
    "setup.region.none": "Belum dipilih",
    "setup.region.pick": "Pilih wilayah server dulu: versi CN dan internasional disiapkan dengan cara yang sangat berbeda.",
    "setup.region.global.note": "Cara loader asli: suntik MikuSB-Patch.dll dan pakai SOCKS5 lokal. Panel hanya mengisikan lokasi dan argumen.",
    "setup.region.cn.launcher": "Versi CN hanya cocok dengan launcher resmi versi lama {0}. Unduh di sini:",
    "setup.region.cn.download": "Unduh",
    "setup.path": "2 Lokasi klien",
    "setup.path.placeholder": "mis. D:\\Games\\Snow, atau …\\data\\game\\Game\\Binaries\\Win64\\Game.exe",
    "setup.path.check": "Periksa lokasi",
    "setup.path.detect": "Deteksi otomatis",
    "setup.path.detecting": "Memindai drive…",
    "setup.path.found": "{0} kandidat ditemukan",
    "setup.check.input": "Jalur klien",
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
    "setup.hosts.elevated": "Berjalan sebagai administrator, jadi hosts dapat ditulis langsung.",
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
    "setup.path.hint": "Jalur ini belum diperiksa; gunakan tombol periksa atau deteksi di atas.","setup.path.required": "Isi jalur klien, atau tekan \"Deteksi otomatis\"."
  }
};
Object.keys(SETUP_STRINGS).forEach((lang) => {
  STRINGS[lang] = Object.assign(STRINGS[lang] || {}, SETUP_STRINGS[lang]);
});

// The overview's runtime row: the host phase (downloading an update / downloading the resource pack /
// running) plus the launch button that only shows up once startup has finished. Kept out of the big
// tables above so this row can grow without editing ten of them.
const RUNTIME_STRINGS = {
  zh_CN: {
    'overview.runtime': '当前服务器状态',
    'overview.runtime.note': '下载更新或资源包时这里会跟着变；初始化完成后可以直接从这里启动游戏。',
    'overview.phase.starting': '初始化中',
    'overview.phase.update': '下载更新',
    'overview.phase.resources': '下载资源',
    'overview.phase.running': '正常运行',
    'overview.launch': '启动游戏'
  },
  en_US: {
    'overview.runtime': 'Server status',
    'overview.runtime.note': 'Follows the host while it downloads an update or the resource pack; once startup has finished the client can be launched from here.',
    'overview.phase.starting': 'Starting up',
    'overview.phase.update': 'Downloading update',
    'overview.phase.resources': 'Downloading resources',
    'overview.phase.running': 'Running',
    'overview.launch': 'Launch game'
  },
  zh_TW: {
    'overview.runtime': '目前伺服器狀態',
    'overview.runtime.note': '下載更新或資源包時這裡會跟著變；初始化完成後可以直接從這裡啟動遊戲。',
    'overview.phase.starting': '初始化中',
    'overview.phase.update': '下載更新',
    'overview.phase.resources': '下載資源',
    'overview.phase.running': '正常執行',
    'overview.launch': '啟動遊戲'
  },
  ja_JP: {
    'overview.runtime': 'サーバーの状態',
    'overview.runtime.note': '更新やリソースパックのダウンロード中はここに表示されます。初期化が終わればここからゲームを起動できます。',
    'overview.phase.starting': '起動準備中',
    'overview.phase.update': '更新をダウンロード中',
    'overview.phase.resources': 'リソースをダウンロード中',
    'overview.phase.running': '正常稼働',
    'overview.launch': 'ゲーム起動'
  },
  ko_KR: {
    'overview.runtime': '서버 상태',
    'overview.runtime.note': '업데이트나 리소스 팩을 내려받는 동안 여기에 표시됩니다. 초기화가 끝나면 여기서 게임을 실행할 수 있습니다.',
    'overview.phase.starting': '초기화 중',
    'overview.phase.update': '업데이트 내려받는 중',
    'overview.phase.resources': '리소스 내려받는 중',
    'overview.phase.running': '정상 실행 중',
    'overview.launch': '게임 실행'
  },
  de_DE: {
    'overview.runtime': 'Serverstatus',
    'overview.runtime.note': 'Zeigt hier den Fortschritt, während ein Update oder das Ressourcenpaket geladen wird; nach dem Start lässt sich der Client von hier aus starten.',
    'overview.phase.starting': 'Startet',
    'overview.phase.update': 'Update wird geladen',
    'overview.phase.resources': 'Ressourcen werden geladen',
    'overview.phase.running': 'Läuft',
    'overview.launch': 'Spiel starten'
  },
  fr_FR: {
    'overview.runtime': 'État du serveur',
    'overview.runtime.note': 'Suit l’hôte pendant le téléchargement d’une mise à jour ou du paquet de ressources ; une fois le démarrage terminé, le client se lance d’ici.',
    'overview.phase.starting': 'Démarrage',
    'overview.phase.update': 'Téléchargement de la mise à jour',
    'overview.phase.resources': 'Téléchargement des ressources',
    'overview.phase.running': 'En marche',
    'overview.launch': 'Lancer le jeu'
  },
  es_ES: {
    'overview.runtime': 'Estado del servidor',
    'overview.runtime.note': 'Sigue al host mientras descarga una actualización o el paquete de recursos; cuando termine el arranque, el cliente se inicia desde aquí.',
    'overview.phase.starting': 'Iniciando',
    'overview.phase.update': 'Descargando actualización',
    'overview.phase.resources': 'Descargando recursos',
    'overview.phase.running': 'En marcha',
    'overview.launch': 'Iniciar juego'
  },
  th_TH: {
    'overview.runtime': 'สถานะเซิร์ฟเวอร์',
    'overview.runtime.note': 'จะแสดงที่นี่ระหว่างดาวน์โหลดอัปเดตหรือชุดทรัพยากร เมื่อเริ่มระบบเสร็จสามารถเปิดเกมได้จากที่นี่',
    'overview.phase.starting': 'กำลังเริ่มต้น',
    'overview.phase.update': 'กำลังดาวน์โหลดอัปเดต',
    'overview.phase.resources': 'กำลังดาวน์โหลดทรัพยากร',
    'overview.phase.running': 'ทำงานปกติ',
    'overview.launch': 'เปิดเกม'
  },
  id_ID: {
    'overview.runtime': 'Status server',
    'overview.runtime.note': 'Mengikuti host saat mengunduh pembaruan atau paket sumber daya; setelah inisialisasi selesai, game bisa dijalankan dari sini.',
    'overview.phase.starting': 'Menyiapkan',
    'overview.phase.update': 'Mengunduh pembaruan',
    'overview.phase.resources': 'Mengunduh sumber daya',
    'overview.phase.running': 'Berjalan normal',
    'overview.launch': 'Jalankan game'
  }
};
Object.keys(RUNTIME_STRINGS).forEach((lang) => {
  STRINGS[lang] = Object.assign(STRINGS[lang] || {}, RUNTIME_STRINGS[lang]);
});

// The CN launch block: the specific-channel build can be started through the hosts block or through the CN
// patch DLL, and the operator has to be able to tell the two apart. Same reason as above for a separate table.
const CNLAUNCH_STRINGS = {
  zh_CN: {
    'setup.cnlaunch': '国服启动方式',
    'setup.cnlaunch.note': '两条路连的都是这台服务器，只是引导方式不同：hosts 改本机 hosts（需管理员权限），DLL 注入把国服补丁塞进客户端（不改 hosts，但要先放好 CN 版补丁 DLL）。点哪个按钮就用哪种方式启动，选择会被记住。',
    'setup.cnlaunch.hosts': '启动游戏（hosts）',
    'setup.cnlaunch.dll': '启动游戏（DLL 注入）',
    'setup.cnlaunch.patch.ready': '国服补丁 DLL 已就位，可以用 DLL 注入启动。',
    'setup.cnlaunch.patch.missing': '国服补丁 DLL 不在 {0}：把 CN 版 MikuSB-Patch.dll 复制到该路径后，DLL 注入才可用。'
  },
  en_US: {
    'setup.cnlaunch': 'CN launch method',
    'setup.cnlaunch.note': 'Both routes reach this server; only the way the client is steered differs. Hosts rewrites this machine\'s hosts file (needs administrator rights), the DLL route injects the CN patch (no hosts change, but the CN build of the patch DLL has to be in place). The button you press decides the route, and the choice is remembered.',
    'setup.cnlaunch.hosts': 'Launch game (hosts)',
    'setup.cnlaunch.dll': 'Launch game (DLL injection)',
    'setup.cnlaunch.patch.ready': 'The CN patch DLL is in place, so the DLL route is ready.',
    'setup.cnlaunch.patch.missing': 'The CN patch DLL is not at {0}; copy the CN build of MikuSB-Patch.dll there to enable the DLL route.'
  },
  zh_TW: {
    'setup.cnlaunch': '國服啟動方式',
    'setup.cnlaunch.note': '兩條路連的都是這台伺服器，只是引導方式不同：hosts 改本機 hosts（需管理員權限），DLL 注入把國服補丁放進用戶端（不改 hosts，但要先放好 CN 版補丁 DLL）。點哪個按鈕就用哪種方式啟動，選擇會被記住。',
    'setup.cnlaunch.hosts': '啟動遊戲（hosts）',
    'setup.cnlaunch.dll': '啟動遊戲（DLL 注入）',
    'setup.cnlaunch.patch.ready': '國服補丁 DLL 已就位，可以用 DLL 注入啟動。',
    'setup.cnlaunch.patch.missing': '國服補丁 DLL 不在 {0}：把 CN 版 MikuSB-Patch.dll 複製到該路徑後，DLL 注入才可用。'
  },
  ja_JP: {
    'setup.cnlaunch': 'CN クライアントの起動方法',
    'setup.cnlaunch.note': 'どちらも同じサーバーに接続します。違いはクライアントの誘導方法だけです。hosts はこの PC の hosts を書き換え（管理者権限が必要）、DLL は CN 用パッチを注入します（hosts は変更しませんが、CN 版 MikuSB-Patch.dll を配置しておく必要があります）。押したボタンの方法で起動し、選択は保存されます。',
    'setup.cnlaunch.hosts': 'ゲーム起動（hosts）',
    'setup.cnlaunch.dll': 'ゲーム起動（DLL 注入）',
    'setup.cnlaunch.patch.ready': 'CN 用パッチ DLL があるため、DLL 注入で起動できます。',
    'setup.cnlaunch.patch.missing': 'CN 用パッチ DLL が {0} にありません。CN 版 MikuSB-Patch.dll をそこへコピーすると DLL 注入が使えます。'
  },
  ko_KR: {
    'setup.cnlaunch': 'CN 실행 방식',
    'setup.cnlaunch.note': '두 방식 모두 같은 서버에 연결되며, 차이는 클라이언트를 유도하는 방법뿐입니다. hosts 는 이 PC의 hosts 파일을 고치고(관리자 권한 필요), DLL 은 CN 패치를 주입합니다(hosts 는 건드리지 않지만 CN 빌드 MikuSB-Patch.dll 이 있어야 합니다). 누른 버튼의 방식으로 실행되며 선택은 저장됩니다.',
    'setup.cnlaunch.hosts': '게임 실행(hosts)',
    'setup.cnlaunch.dll': '게임 실행(DLL 주입)',
    'setup.cnlaunch.patch.ready': 'CN 패치 DLL 이 준비되어 DLL 주입으로 실행할 수 있습니다.',
    'setup.cnlaunch.patch.missing': 'CN 패치 DLL 이 {0} 에 없습니다. CN 빌드 MikuSB-Patch.dll 을 복사하면 DLL 주입을 쓸 수 있습니다.'
  },
  de_DE: {
    'setup.cnlaunch': 'CN-Startmethode',
    'setup.cnlaunch.note': 'Beide Wege erreichen denselben Server; nur die Steuerung des Clients unterscheidet sich. hosts schreibt die hosts-Datei dieses Rechners um (Administratorrechte nötig), die DLL-Variante injiziert den CN-Patch (keine hosts-Änderung, dafür muss der CN-Build der Patch-DLL bereitliegen). Der gedrückte Knopf entscheidet, und die Wahl wird gemerkt.',
    'setup.cnlaunch.hosts': 'Spiel starten (hosts)',
    'setup.cnlaunch.dll': 'Spiel starten (DLL-Injektion)',
    'setup.cnlaunch.patch.ready': 'Die CN-Patch-DLL liegt bereit, die DLL-Variante ist einsatzfähig.',
    'setup.cnlaunch.patch.missing': 'Die CN-Patch-DLL fehlt unter {0}; kopiere den CN-Build von MikuSB-Patch.dll dorthin, um die DLL-Variante zu nutzen.'
  },
  fr_FR: {
    'setup.cnlaunch': 'Méthode de lancement CN',
    'setup.cnlaunch.note': 'Les deux voies mènent au même serveur ; seule la façon de guider le client change. hosts réécrit le fichier hosts de cette machine (droits administrateur requis), la voie DLL injecte le patch CN (aucune modification de hosts, mais le build CN de la DLL doit être en place). Le bouton choisi décide de la voie, et le choix est mémorisé.',
    'setup.cnlaunch.hosts': 'Lancer le jeu (hosts)',
    'setup.cnlaunch.dll': 'Lancer le jeu (injection DLL)',
    'setup.cnlaunch.patch.ready': 'La DLL de patch CN est en place, la voie DLL est disponible.',
    'setup.cnlaunch.patch.missing': 'La DLL de patch CN est absente de {0} ; copiez-y le build CN de MikuSB-Patch.dll pour activer la voie DLL.'
  },
  es_ES: {
    'setup.cnlaunch': 'Método de inicio CN',
    'setup.cnlaunch.note': 'Ambas rutas llegan al mismo servidor; solo cambia cómo se guía al cliente. hosts reescribe el archivo hosts de este equipo (requiere permisos de administrador), la ruta DLL inyecta el parche CN (sin tocar hosts, pero el build CN de la DLL debe estar colocado). El botón que pulses decide la ruta, y la elección se recuerda.',
    'setup.cnlaunch.hosts': 'Iniciar juego (hosts)',
    'setup.cnlaunch.dll': 'Iniciar juego (inyección DLL)',
    'setup.cnlaunch.patch.ready': 'La DLL del parche CN está colocada, la ruta DLL está lista.',
    'setup.cnlaunch.patch.missing': 'La DLL del parche CN no está en {0}; copia allí el build CN de MikuSB-Patch.dll para habilitar la ruta DLL.'
  },
  th_TH: {
    'setup.cnlaunch': 'วิธีเปิดเกม CN',
    'setup.cnlaunch.note': 'ทั้งสองวิธีเชื่อมต่อเซิร์ฟเวอร์เดียวกัน ต่างกันแค่วิธีชี้นำไคลเอนต์: hosts จะแก้ไฟล์ hosts ของเครื่องนี้ (ต้องใช้สิทธิ์ผู้ดูแลระบบ) ส่วน DLL จะฉีดแพตช์ CN ให้ (ไม่แตะ hosts แต่ต้องวาง MikuSB-Patch.dll เวอร์ชัน CN ไว้ให้เรียบร้อย) ปุ่มที่กดจะเป็นตัวเลือกวิธี และระบบจะจำไว้',
    'setup.cnlaunch.hosts': 'เปิดเกม (hosts)',
    'setup.cnlaunch.dll': 'เปิดเกม (ฉีด DLL)',
    'setup.cnlaunch.patch.ready': 'มีแพตช์ DLL เวอร์ชัน CN แล้ว จึงใช้วิธีฉีด DLL ได้',
    'setup.cnlaunch.patch.missing': 'ไม่พบแพตช์ DLL เวอร์ชัน CN ที่ {0} คัดลอก MikuSB-Patch.dll เวอร์ชัน CN ไปวางแล้วจึงจะใช้วิธีฉีด DLL ได้'
  },
  id_ID: {
    'setup.cnlaunch': 'Cara menjalankan CN',
    'setup.cnlaunch.note': 'Kedua jalur menuju server yang sama; yang berbeda hanya cara mengarahkan klien. hosts menulis ulang berkas hosts mesin ini (perlu hak administrator), jalur DLL menyuntikkan patch CN (tidak mengubah hosts, tetapi build CN MikuSB-Patch.dll harus sudah ada). Tombol yang ditekan menentukan jalurnya, dan pilihannya diingat.',
    'setup.cnlaunch.hosts': 'Jalankan game (hosts)',
    'setup.cnlaunch.dll': 'Jalankan game (suntik DLL)',
    'setup.cnlaunch.patch.ready': 'DLL patch CN sudah tersedia, jalur DLL siap dipakai.',
    'setup.cnlaunch.patch.missing': 'DLL patch CN tidak ada di {0}; salin build CN MikuSB-Patch.dll ke sana agar jalur DLL bisa dipakai.'
  }
};
Object.keys(CNLAUNCH_STRINGS).forEach((lang) => {
  STRINGS[lang] = Object.assign(STRINGS[lang] || {}, CNLAUNCH_STRINGS[lang]);
});

// Wizard gating copy. Kept apart from SETUP_STRINGS so the step rules stay in one readable block: each
// step owns the sentence that explains why the next control is still disabled.
const WIZARD_STRINGS = {
  zh_CN: {
    'setup.channel.ready': '该渠道的补丁 DLL 已就位。',
    'setup.channel.preparing': '正在准备该渠道的补丁 DLL…',
    'setup.channel.failed': '准备补丁 DLL 失败',
    'setup.mode.note': 'DLL 注入不写 hosts；hosts 模式会改写本机 hosts。选择会写进配置。',
    'setup.locked': '先完成上一步'
  },
  en_US: {
    'setup.channel.ready': 'The patch DLL for this channel is in place.',
    'setup.channel.preparing': 'Preparing the patch DLL for this channel…',
    'setup.channel.failed': 'Preparing the patch DLL failed',
    'setup.mode.note': 'DLL injection leaves hosts alone; hosts mode rewrites this machine hosts file. The choice is saved with the config.',
    'setup.locked': 'Finish the previous step first'
  },
  zh_TW: {
    'setup.channel.ready': '該渠道的補丁 DLL 已就位。',
    'setup.channel.preparing': '正在準備該渠道的補丁 DLL…',
    'setup.channel.failed': '準備補丁 DLL 失敗',
    'setup.mode.note': 'DLL 注入不寫 hosts；hosts 模式會改寫本機 hosts。選擇會寫進設定。',
    'setup.locked': '先完成上一步'
  },
  ja_JP: {
    'setup.channel.ready': 'このチャネルのパッチ DLL は準備済みです。',
    'setup.channel.preparing': 'このチャネルのパッチ DLL を準備しています…',
    'setup.channel.failed': 'パッチ DLL の準備に失敗しました',
    'setup.mode.note': 'DLL 注入は hosts を変更しません。hosts モードはこの PC の hosts を書き換えます。選択は設定に保存されます。',
    'setup.locked': '先に前の手順を完了してください'
  },
  ko_KR: {
    'setup.channel.ready': '이 채널의 패치 DLL이 준비되었습니다.',
    'setup.channel.preparing': '이 채널의 패치 DLL을 준비하는 중…',
    'setup.channel.failed': '패치 DLL 준비에 실패했습니다',
    'setup.mode.note': 'DLL 주입은 hosts를 건드리지 않습니다. hosts 모드는 이 PC의 hosts 파일을 다시 씁니다. 선택은 설정에 저장됩니다.',
    'setup.locked': '먼저 이전 단계를 완료하세요'
  },
  de_DE: {
    'setup.channel.ready': 'Die Patch-DLL für diesen Kanal liegt bereit.',
    'setup.channel.preparing': 'Die Patch-DLL für diesen Kanal wird vorbereitet…',
    'setup.channel.failed': 'Patch-DLL konnte nicht vorbereitet werden',
    'setup.mode.note': 'DLL-Injection lässt die hosts-Datei unverändert; der hosts-Modus schreibt sie neu. Die Auswahl wird in der Konfiguration gespeichert.',
    'setup.locked': 'Schließe zuerst den vorherigen Schritt ab'
  },
  fr_FR: {
    'setup.channel.ready': 'La DLL de patch de ce canal est en place.',
    'setup.channel.preparing': 'Préparation de la DLL de patch de ce canal…',
    'setup.channel.failed': 'La préparation de la DLL de patch a échoué',
    'setup.mode.note': 'L injection DLL ne touche pas au fichier hosts ; le mode hosts réécrit celui de cette machine. Le choix est enregistré dans la configuration.',
    'setup.locked': 'Terminez d abord l étape précédente'
  },
  es_ES: {
    'setup.channel.ready': 'La DLL de parche de este canal está lista.',
    'setup.channel.preparing': 'Preparando la DLL de parche de este canal…',
    'setup.channel.failed': 'No se pudo preparar la DLL de parche',
    'setup.mode.note': 'La inyección DLL no toca el archivo hosts; el modo hosts reescribe el de este equipo. La elección se guarda en la configuración.',
    'setup.locked': 'Completa primero el paso anterior'
  },
  th_TH: {
    'setup.channel.ready': 'แพตช์ DLL ของช่องทางนี้พร้อมแล้ว',
    'setup.channel.preparing': 'กำลังเตรียมแพตช์ DLL ของช่องทางนี้…',
    'setup.channel.failed': 'เตรียมแพตช์ DLL ไม่สำเร็จ',
    'setup.mode.note': 'การฉีด DLL จะไม่แตะไฟล์ hosts ส่วนโหมด hosts จะเขียนไฟล์ hosts ของเครื่องนี้ใหม่ ระบบจะบันทึกตัวเลือกไว้ในการตั้งค่า',
    'setup.locked': 'ทำขั้นตอนก่อนหน้าให้เสร็จก่อน'
  },
  id_ID: {
    'setup.channel.ready': 'DLL patch untuk kanal ini sudah siap.',
    'setup.channel.preparing': 'Menyiapkan DLL patch untuk kanal ini…',
    'setup.channel.failed': 'Gagal menyiapkan DLL patch',
    'setup.mode.note': 'Injeksi DLL tidak menyentuh hosts; mode hosts menulis ulang berkas hosts mesin ini. Pilihannya disimpan di konfigurasi.',
    'setup.locked': 'Selesaikan langkah sebelumnya dulu'
  }
};
Object.keys(WIZARD_STRINGS).forEach((lang) => {
  STRINGS[lang] = Object.assign(STRINGS[lang] || {}, WIZARD_STRINGS[lang]);
});

// ---------------------------------------------------------------------------------------------
// setup verdicts
// The server answers with codes, not prose (path checks, channel prep, next steps). It used to send
// finished Chinese sentences, which is why an English panel showed "Install root 找不到安装根目录下的
// data\game" and why adding a language meant editing C#. Everything the operator reads is now built here.
// ---------------------------------------------------------------------------------------------
const CODE_STRINGS = {
  zh_CN: {
    'setup.check.input.empty': '客户端路径为空。',
    'setup.check.locate.missing': '这个路径下找不到 {0}。',
    'setup.check.exe.missing': 'Game.exe 不存在。',
    'setup.check.name.mismatch': '可执行文件名不是 Game.exe，游戏可能不是尘白禁区。',
    'setup.check.userdir.missing': '找不到包含 {0} 的目录（-userdir 需要它）。',
    'setup.check.paks.missing': '缺少 {0}（游戏资源未安装或路径不对）。',
    'setup.check.paks.no_paks': '目录存在，但里面没有 .pak 文件。',
    'setup.check.install.missing': '找不到这个客户端的安装根目录。',
    'setup.channel.code.cn_ready': '国服补丁 DLL 已就位：{0}',
    'setup.channel.code.cn_missing': '国服补丁 DLL 不在 {0}：把 CN 版 MikuSB-Patch.dll 放到该路径后再继续。',
    'setup.channel.code.ready': '国际服补丁 DLL 已就位。',
    'setup.channel.code.missing': '国际服补丁 DLL 仍未下载成功，检查网络后重试。',
    'setup.channel.code.download_failed': '下载国际服补丁 DLL 失败，检查网络后重试（详情见日志）。',
    'setup.next.cn_dll': '国服：DLL 注入模式，用下面的按钮启动游戏。',
    'setup.next.cn_hosts': '国服：hosts 模式，直接用官方启动器或游戏 exe 启动。',
    'setup.next.patch_dir': '把 Patch\\MikuSB-Patch.dll 放到 Config 同级目录（首次会自动下载）。',
    'setup.next.launch': '用下面的按钮启动游戏。',
    'setup.error.invalid_path': '客户端路径没有通过检查。',
    'setup.error.region_required': '请先选择服务器区域（国服或国际服）。'
  },
  zh_TW: {
    'setup.check.input.empty': '用戶端路徑為空。',
    'setup.check.locate.missing': '這個路徑下找不到 {0}。',
    'setup.check.exe.missing': 'Game.exe 不存在。',
    'setup.check.name.mismatch': '可執行檔名不是 Game.exe，遊戲可能不是塵白禁區。',
    'setup.check.userdir.missing': '找不到包含 {0} 的目錄（-userdir 需要它）。',
    'setup.check.paks.missing': '缺少 {0}（遊戲資源未安裝或路徑不對）。',
    'setup.check.paks.no_paks': '目錄存在，但裡面沒有 .pak 檔案。',
    'setup.check.install.missing': '找不到這個用戶端的安裝根目錄。',
    'setup.channel.code.cn_ready': '國服補丁 DLL 已就位：{0}',
    'setup.channel.code.cn_missing': '國服補丁 DLL 不在 {0}：把 CN 版 MikuSB-Patch.dll 放到該路徑後再繼續。',
    'setup.channel.code.ready': '國際服補丁 DLL 已就位。',
    'setup.channel.code.missing': '國際服補丁 DLL 仍未下載成功，檢查網路後重試。',
    'setup.channel.code.download_failed': '下載國際服補丁 DLL 失敗，檢查網路後重試（詳情見日誌）。',
    'setup.next.cn_dll': '國服：DLL 注入模式，用下面的按鈕啟動遊戲。',
    'setup.next.cn_hosts': '國服：hosts 模式，直接用官方啟動器或遊戲 exe 啟動。',
    'setup.next.patch_dir': '把 Patch\\MikuSB-Patch.dll 放到 Config 同層目錄（首次會自動下載）。',
    'setup.next.launch': '用下面的按鈕啟動遊戲。',
    'setup.error.invalid_path': '用戶端路徑沒有通過檢查。',
    'setup.error.region_required': '請先選擇伺服器區域（國服或國際服）。'
  },
  ja_JP: {
    'setup.check.input.empty': 'クライアントのパスが空です。',
    'setup.check.locate.missing': 'このパスに {0} が見つかりません。',
    'setup.check.exe.missing': 'Game.exe が存在しません。',
    'setup.check.name.mismatch': '実行ファイル名が Game.exe ではありません。Snowbreak ではない可能性があります。',
    'setup.check.userdir.missing': '{0} を含むフォルダーが見つかりません（-userdir に必要です）。',
    'setup.check.paks.missing': '{0} がありません（ゲーム資源が未インストールか、パスが違います）。',
    'setup.check.paks.no_paks': 'フォルダーはありますが、.pak ファイルがありません。',
    'setup.check.install.missing': 'このクライアントのインストール先が見つかりません。',
    'setup.channel.code.cn_ready': 'CN 用パッチ DLL は配置済みです: {0}',
    'setup.channel.code.cn_missing': 'CN 用パッチ DLL が {0} にありません。CN 版 MikuSB-Patch.dll を置いてから続行してください。',
    'setup.channel.code.ready': 'グローバル用パッチ DLL は配置済みです。',
    'setup.channel.code.missing': 'グローバル用パッチ DLL を取得できませんでした。ネットワークを確認して再試行してください。',
    'setup.channel.code.download_failed': 'グローバル用パッチ DLL のダウンロードに失敗しました。ネットワークを確認して再試行してください（詳細はログ）。',
    'setup.next.cn_dll': 'CN: DLL インジェクションモード。下のボタンで起動します。',
    'setup.next.cn_hosts': 'CN: hosts モード。公式ランチャーまたはゲーム exe で起動します。',
    'setup.next.patch_dir': 'Patch\\MikuSB-Patch.dll を Config と同じ場所に置いてください（初回は自動でダウンロードされます）。',
    'setup.next.launch': '下のボタンでゲームを起動します。',
    'setup.error.invalid_path': 'クライアントのパスがチェックを通りませんでした。',
    'setup.error.region_required': '先にサーバー地域（CN またはグローバル）を選んでください。'
  },
  ko_KR: {
    'setup.check.input.empty': '클라이언트 경로가 비어 있습니다.',
    'setup.check.locate.missing': '이 경로에서 {0} 을(를) 찾을 수 없습니다.',
    'setup.check.exe.missing': 'Game.exe 가 없습니다.',
    'setup.check.name.mismatch': '실행 파일 이름이 Game.exe 가 아닙니다. Snowbreak 가 아닐 수 있습니다.',
    'setup.check.userdir.missing': '{0} 이(가) 포함된 폴더를 찾을 수 없습니다 (-userdir 에 필요).',
    'setup.check.paks.missing': '{0} 이(가) 없습니다 (게임 리소스가 설치되지 않았거나 경로가 다릅니다).',
    'setup.check.paks.no_paks': '폴더는 있지만 .pak 파일이 없습니다.',
    'setup.check.install.missing': '이 클라이언트의 설치 루트를 찾을 수 없습니다.',
    'setup.channel.code.cn_ready': 'CN 패치 DLL 이 준비되었습니다: {0}',
    'setup.channel.code.cn_missing': 'CN 패치 DLL 이 {0} 에 없습니다. CN 용 MikuSB-Patch.dll 을 넣은 뒤 계속하세요.',
    'setup.channel.code.ready': '글로벌 패치 DLL 이 준비되었습니다.',
    'setup.channel.code.missing': '글로벌 패치 DLL 을 아직 받지 못했습니다. 네트워크를 확인하고 다시 시도하세요.',
    'setup.channel.code.download_failed': '글로벌 패치 DLL 다운로드에 실패했습니다. 네트워크를 확인하고 다시 시도하세요 (자세한 내용은 로그).',
    'setup.next.cn_dll': 'CN: DLL 주입 모드입니다. 아래 버튼으로 게임을 시작하세요.',
    'setup.next.cn_hosts': 'CN: hosts 모드입니다. 공식 런처나 게임 exe 로 시작하세요.',
    'setup.next.patch_dir': 'Patch\\MikuSB-Patch.dll 을 Config 와 같은 위치에 두세요 (처음에는 자동으로 받습니다).',
    'setup.next.launch': '아래 버튼으로 게임을 시작하세요.',
    'setup.error.invalid_path': '클라이언트 경로가 검사를 통과하지 못했습니다.',
    'setup.error.region_required': '먼저 서버 지역(CN 또는 글로벌)을 선택하세요.'
  },
  en_US: {
    'setup.check.input.empty': 'The client path is empty.',
    'setup.check.locate.missing': 'No {0} under this path.',
    'setup.check.exe.missing': 'Game.exe does not exist.',
    'setup.check.name.mismatch': 'The executable is not Game.exe; this may not be Snowbreak.',
    'setup.check.userdir.missing': 'No folder containing {0} (-userdir needs it).',
    'setup.check.paks.missing': '{0} is missing (the client resources are not installed, or the path is wrong).',
    'setup.check.paks.no_paks': 'The folder exists but holds no .pak file.',
    'setup.check.install.missing': 'Cannot find the install root for this client.',
    'setup.channel.code.cn_ready': 'CN patch DLL is in place: {0}',
    'setup.channel.code.cn_missing': 'CN patch DLL is not at {0}. Copy the CN MikuSB-Patch.dll there and continue.',
    'setup.channel.code.ready': 'Global patch DLL is in place.',
    'setup.channel.code.missing': 'The global patch DLL is still missing. Check the network and try again.',
    'setup.channel.code.download_failed': 'Downloading the global patch DLL failed. Check the network and try again (details in the log).',
    'setup.next.cn_dll': 'CN: DLL injection mode. Start the game with the button below.',
    'setup.next.cn_hosts': 'CN: hosts mode. Start the game with the official launcher or the game exe.',
    'setup.next.patch_dir': 'Keep Patch\\MikuSB-Patch.dll next to Config (it downloads automatically the first time).',
    'setup.next.launch': 'Start the game with the button below.',
    'setup.error.invalid_path': 'The client path did not pass the checks.',
    'setup.error.region_required': 'Pick a server region first (CN or global).'
  },
  fr_FR: {
    'setup.check.input.empty': 'Le chemin du client est vide.',
    'setup.check.locate.missing': 'Aucun {0} sous ce chemin.',
    'setup.check.exe.missing': 'Game.exe est absent.',
    'setup.check.name.mismatch': "L'exécutable n'est pas Game.exe ; ce n'est peut-être pas Snowbreak.",
    'setup.check.userdir.missing': 'Aucun dossier ne contient {0} (nécessaire pour -userdir).',
    'setup.check.paks.missing': '{0} est absent (ressources du client non installées ou chemin incorrect).',
    'setup.check.paks.no_paks': 'Le dossier existe mais ne contient aucun fichier .pak.',
    'setup.check.install.missing': "Impossible de trouver la racine d'installation de ce client.",
    'setup.channel.code.cn_ready': 'DLL de patch CN en place : {0}',
    'setup.channel.code.cn_missing': "La DLL de patch CN n'est pas dans {0}. Copiez-y le MikuSB-Patch.dll CN puis continuez.",
    'setup.channel.code.ready': 'DLL de patch globale en place.',
    'setup.channel.code.missing': 'La DLL de patch globale est toujours absente. Vérifiez le réseau et réessayez.',
    'setup.channel.code.download_failed': 'Le téléchargement de la DLL de patch globale a échoué. Vérifiez le réseau et réessayez (détails dans le journal).',
    'setup.next.cn_dll': 'CN : mode injection DLL. Lancez le jeu avec le bouton ci-dessous.',
    'setup.next.cn_hosts': 'CN : mode hosts. Lancez le jeu avec le lanceur officiel ou Game.exe.',
    'setup.next.patch_dir': 'Placez Patch\\MikuSB-Patch.dll à côté de Config (téléchargé automatiquement la première fois).',
    'setup.next.launch': 'Lancez le jeu avec le bouton ci-dessous.',
    'setup.error.invalid_path': "Le chemin du client n'a pas passé les vérifications.",
    'setup.error.region_required': "Choisissez d'abord une région de serveur (CN ou globale)."
  },
  es_ES: {
    'setup.check.input.empty': 'La ruta del cliente está vacía.',
    'setup.check.locate.missing': 'No hay {0} en esta ruta.',
    'setup.check.exe.missing': 'Game.exe no existe.',
    'setup.check.name.mismatch': 'El ejecutable no es Game.exe; puede que no sea Snowbreak.',
    'setup.check.userdir.missing': 'No hay ninguna carpeta que contenga {0} (lo necesita -userdir).',
    'setup.check.paks.missing': 'Falta {0} (los recursos del cliente no están instalados o la ruta es incorrecta).',
    'setup.check.paks.no_paks': 'La carpeta existe pero no contiene ningún archivo .pak.',
    'setup.check.install.missing': 'No se encuentra la raíz de instalación de este cliente.',
    'setup.channel.code.cn_ready': 'DLL de parche CN lista: {0}',
    'setup.channel.code.cn_missing': 'La DLL de parche CN no está en {0}. Copia allí el MikuSB-Patch.dll de CN y continúa.',
    'setup.channel.code.ready': 'DLL de parche global lista.',
    'setup.channel.code.missing': 'La DLL de parche global sigue faltando. Comprueba la red y vuelve a intentarlo.',
    'setup.channel.code.download_failed': 'No se pudo descargar la DLL de parche global. Comprueba la red y vuelve a intentarlo (detalles en el registro).',
    'setup.next.cn_dll': 'CN: modo inyección de DLL. Inicia el juego con el botón de abajo.',
    'setup.next.cn_hosts': 'CN: modo hosts. Inicia el juego con el lanzador oficial o Game.exe.',
    'setup.next.patch_dir': 'Coloca Patch\\MikuSB-Patch.dll junto a Config (se descarga automáticamente la primera vez).',
    'setup.next.launch': 'Inicia el juego con el botón de abajo.',
    'setup.error.invalid_path': 'La ruta del cliente no superó las comprobaciones.',
    'setup.error.region_required': 'Elige primero una región de servidor (CN o global).'
  },
  de_DE: {
    'setup.check.input.empty': 'Der Client-Pfad ist leer.',
    'setup.check.locate.missing': 'Unter diesem Pfad wurde kein {0} gefunden.',
    'setup.check.exe.missing': 'Game.exe ist nicht vorhanden.',
    'setup.check.name.mismatch': 'Die Programmdatei ist nicht Game.exe; möglicherweise ist das nicht Snowbreak.',
    'setup.check.userdir.missing': 'Kein Ordner enthält {0} (wird für -userdir benötigt).',
    'setup.check.paks.missing': '{0} fehlt (Client-Ressourcen nicht installiert oder Pfad falsch).',
    'setup.check.paks.no_paks': 'Der Ordner existiert, enthält aber keine .pak-Datei.',
    'setup.check.install.missing': 'Das Installationsverzeichnis dieses Clients wurde nicht gefunden.',
    'setup.channel.code.cn_ready': 'CN-Patch-DLL liegt bereit: {0}',
    'setup.channel.code.cn_missing': 'Die CN-Patch-DLL liegt nicht unter {0}. Kopiere die CN-MikuSB-Patch.dll dorthin und fahre fort.',
    'setup.channel.code.ready': 'Globale Patch-DLL liegt bereit.',
    'setup.channel.code.missing': 'Die globale Patch-DLL fehlt weiterhin. Prüfe das Netzwerk und versuche es erneut.',
    'setup.channel.code.download_failed': 'Das Herunterladen der globalen Patch-DLL ist fehlgeschlagen. Prüfe das Netzwerk und versuche es erneut (Details im Log).',
    'setup.next.cn_dll': 'CN: DLL-Injektionsmodus. Starte das Spiel mit der Schaltfläche unten.',
    'setup.next.cn_hosts': 'CN: hosts-Modus. Starte das Spiel mit dem offiziellen Launcher oder Game.exe.',
    'setup.next.patch_dir': 'Lege Patch\\MikuSB-Patch.dll neben Config (wird beim ersten Mal automatisch geladen).',
    'setup.next.launch': 'Starte das Spiel mit der Schaltfläche unten.',
    'setup.error.invalid_path': 'Der Client-Pfad hat die Prüfungen nicht bestanden.',
    'setup.error.region_required': 'Wähle zuerst eine Serverregion (CN oder global).'
  },
  th_TH: {
    'setup.check.input.empty': 'พาธไคลเอนต์ว่างอยู่',
    'setup.check.locate.missing': 'ไม่พบ {0} ใต้พาธนี้',
    'setup.check.exe.missing': 'ไม่พบ Game.exe',
    'setup.check.name.mismatch': 'ชื่อไฟล์เรียกไม่ใช่ Game.exe อาจไม่ใช่ Snowbreak',
    'setup.check.userdir.missing': 'ไม่พบโฟลเดอร์ที่มี {0} (จำเป็นสำหรับ -userdir)',
    'setup.check.paks.missing': 'ไม่พบ {0} (ทรัพยากรเกมยังไม่ได้ติดตั้งหรือพาธไม่ถูกต้อง)',
    'setup.check.paks.no_paks': 'โฟลเดอร์มีอยู่แต่ไม่มีไฟล์ .pak',
    'setup.check.install.missing': 'ไม่พบโฟลเดอร์ติดตั้งของไคลเอนต์นี้',
    'setup.channel.code.cn_ready': 'DLL แพตช์ CN พร้อมแล้ว: {0}',
    'setup.channel.code.cn_missing': 'ไม่พบ DLL แพตช์ CN ที่ {0} ให้คัดลอก MikuSB-Patch.dll เวอร์ชัน CN ไปไว้ที่นั่นแล้วดำเนินการต่อ',
    'setup.channel.code.ready': 'DLL แพตช์โกลบอลพร้อมแล้ว',
    'setup.channel.code.missing': 'ยังดาวน์โหลด DLL แพตช์โกลบอลไม่ได้ ตรวจสอบเครือข่ายแล้วลองอีกครั้ง',
    'setup.channel.code.download_failed': 'ดาวน์โหลด DLL แพตช์โกลบอลล้มเหลว ตรวจสอบเครือข่ายแล้วลองอีกครั้ง (รายละเอียดในบันทึก)',
    'setup.next.cn_dll': 'CN: โหมดฉีด DLL เริ่มเกมด้วยปุ่มด้านล่าง',
    'setup.next.cn_hosts': 'CN: โหมด hosts เริ่มเกมด้วยตัวเรียกเกมอย่างเป็นทางการหรือ Game.exe',
    'setup.next.patch_dir': 'วาง Patch\\MikuSB-Patch.dll ไว้ข้าง Config (ครั้งแรกจะดาวน์โหลดอัตโนมัติ)',
    'setup.next.launch': 'เริ่มเกมด้วยปุ่มด้านล่าง',
    'setup.error.invalid_path': 'พาธไคลเอนต์ไม่ผ่านการตรวจสอบ',
    'setup.error.region_required': 'เลือกภูมิภาคเซิร์ฟเวอร์ก่อน (CN หรือโกลบอล)'
  },
  id_ID: {
    'setup.check.input.empty': 'Jalur klien kosong.',
    'setup.check.locate.missing': 'Tidak ada {0} di jalur ini.',
    'setup.check.exe.missing': 'Game.exe tidak ada.',
    'setup.check.name.mismatch': 'Nama berkas eksekusi bukan Game.exe; ini mungkin bukan Snowbreak.',
    'setup.check.userdir.missing': 'Tidak ada folder yang berisi {0} (dibutuhkan oleh -userdir).',
    'setup.check.paks.missing': '{0} tidak ada (sumber daya klien belum terpasang atau jalurnya salah).',
    'setup.check.paks.no_paks': 'Folder ada tetapi tidak berisi berkas .pak.',
    'setup.check.install.missing': 'Tidak menemukan folder instalasi klien ini.',
    'setup.channel.code.cn_ready': 'DLL patch CN sudah siap: {0}',
    'setup.channel.code.cn_missing': 'DLL patch CN tidak ada di {0}. Salin MikuSB-Patch.dll versi CN ke sana lalu lanjutkan.',
    'setup.channel.code.ready': 'DLL patch global sudah siap.',
    'setup.channel.code.missing': 'DLL patch global masih belum ada. Periksa jaringan lalu coba lagi.',
    'setup.channel.code.download_failed': 'Gagal mengunduh DLL patch global. Periksa jaringan lalu coba lagi (detail di log).',
    'setup.next.cn_dll': 'CN: mode injeksi DLL. Jalankan game dengan tombol di bawah.',
    'setup.next.cn_hosts': 'CN: mode hosts. Jalankan game dengan launcher resmi atau Game.exe.',
    'setup.next.patch_dir': 'Letakkan Patch\\MikuSB-Patch.dll di samping Config (otomatis diunduh saat pertama kali).',
    'setup.next.launch': 'Jalankan game dengan tombol di bawah.',
    'setup.error.invalid_path': 'Jalur klien tidak lolos pemeriksaan.',
    'setup.error.region_required': 'Pilih wilayah server dulu (CN atau global).'
  }
};
Object.keys(CODE_STRINGS).forEach((lang) => {
  STRINGS[lang] = Object.assign(STRINGS[lang] || {}, CODE_STRINGS[lang]);
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
  const runtime = runtimeState(health);
  setActiveTab('overview');
  view.innerHTML = `
    <h2>${esc(t('overview.title'))}</h2>
    <div class="cards">
      <div class="card"><div class="card-label">${esc(t('overview.accounts'))}</div><div class="card-value">${esc(overview.accounts)}</div></div>
      <div class="card"><div class="card-label">${esc(t('overview.online'))}</div><div class="card-value">${esc(overview.online)}</div></div>
      <div class="card"><div class="card-label">${esc(t('overview.commands'))}</div><div class="card-value">${esc(overview.commands)}</div></div>
      <div class="card"><div class="card-label">${esc(t('overview.uptime'))}</div><div class="card-value">${esc(formatDuration(health.uptime_sec || 0))}</div></div>
    </div>
    <h3 class="section">${esc(t('overview.runtime'))}</h3>
    <div class="setup-row">
      <span id="runtime-pill" class="${runtime.cls}">${esc(runtime.text)}</span>
      <span id="runtime-launch-slot">${health.initialized
        ? '<button id="runtime-launch" class="primary">' + esc(t('overview.launch')) + '</button>'
        : ''}</span>
      <span id="runtime-msg" class="note"></span>
    </div>
    <p class="note">${esc(t('overview.runtime.note'))}</p>
    <h3 class="section">${esc(t('overview.server'))}</h3>
    <table class="kv">
      <tr><th>${esc(t('overview.name'))}</th><td>${esc(overview.name || health.name || 'MikuSB')}</td></tr>
      <tr><th>${esc(t('overview.version'))}</th><td>${esc(health.version || '?')}</td></tr>
      <tr><th>${esc(t('overview.database'))}</th><td>${health.db_loaded
        ? '<span class="pill ok">' + esc(t('overview.loaded')) + '</span>'
        : '<span class="pill warn">' + esc(t('overview.loading')) + '</span>'}</td></tr>
    </table>
    <p class="note">${t('overview.note')}</p>
    <h3 class="section">${esc(t('update.title'))}</h3>
    <div id="update-box" class="update-box">
      <div class="update-line">
        <span>${esc(t('update.current'))}: <b>${esc(health.version || '?')}</b></span>
        <span id="update-latest" class="note">${esc(t('update.idle'))}</span>
      </div>
      <div class="setup-row">
        <button id="update-check">${esc(t('update.check'))}</button>
        <button id="update-apply" class="primary" disabled>${esc(t('update.apply'))}</button>
        <span id="update-msg" class="note"></span>
      </div>
    </div>
  `;
  setStatus(true, t('status.running', health.version || '?', formatDuration(health.uptime_sec || 0)));
  wireUpdate();
  wireRuntime();
}

// The host reports its phase through /health because the panel is served before the command layer is
// wired (see Common/Util/ServerRuntime.cs). Anything that is not update/resources means "running", and
// "running" before MarkInitialized is still the startup went through.
function runtimeState(health) {
  const phase = (health && health.phase) || 'running';
  if (phase === 'update') return { text: t('overview.phase.update'), cls: 'pill warn' };
  if (phase === 'resources') return { text: t('overview.phase.resources'), cls: 'pill warn' };
  if (health && health.initialized === false) return { text: t('overview.phase.starting'), cls: 'pill' };
  return { text: t('overview.phase.running'), cls: 'pill ok' };
}

function attachLaunch(button) {
  if (!button) return;
  button.addEventListener('click', async () => {
    const message = document.getElementById('runtime-msg');
    button.disabled = true;
    if (message) {
      message.className = 'note';
      message.textContent = t('setup.launch.running');
    }
    try {
      const data = await api('/setup/launch', { method: 'POST', body: {} });
      const detail = (data && data.command) || '';
      const ok = !!(data && data.success);
      if (message) {
        message.className = ok ? 'note' : 'note warn-text';
        message.textContent = ok
          ? t('setup.launch.done', detail)
          : t('setup.launch.failed', ((data && data.messages) || []).filter(Boolean).join(' ') || detail);
      }
      if (!ok) button.disabled = false;
    } catch (error) {
      if (message) {
        message.className = 'note warn-text';
        message.textContent = t('setup.launch.failed',
          (error.body && (error.body.message || error.body.code)) || error.message);
      }
      button.disabled = false;
    }
  });
}

function wireRuntime() {
  attachLaunch(document.getElementById('runtime-launch'));
  scheduleRuntimePoll();
}

// The row has to stay current while the host is still downloading (a big resource package takes a while,
// and the launch button only appears once startup has finished), so poll /health while it is on screen.
function scheduleRuntimePoll() {
  clearTimeout(scheduleRuntimePoll.timer);
  scheduleRuntimePoll.timer = setTimeout(async () => {
    const pill = document.getElementById('runtime-pill');
    if (!pill) return; // navigated to another page
    try {
      const health = await api('/health');
      const next = runtimeState(health);
      pill.className = next.cls;
      pill.textContent = next.text;
      const slot = document.getElementById('runtime-launch-slot');
      if (slot) {
        const existing = document.getElementById('runtime-launch');
        if (health.initialized && !existing) {
          slot.innerHTML = '<button id="runtime-launch" class="primary">' + esc(t('overview.launch')) + '</button>';
          attachLaunch(document.getElementById('runtime-launch'));
        } else if (!health.initialized && existing) {
          slot.innerHTML = '';
        }
      }
    } catch (error) {
      if (error.message === 'unauthorized') return;
    }
    scheduleRuntimePoll();
  }, 5000);
}

// ---------------------------------------------------------------------------------------------
// update
// ---------------------------------------------------------------------------------------------

function updateMessage(text, isError) {
  const node = document.getElementById('update-msg');
  if (!node) return;
  node.className = isError ? 'note warn-text' : 'note';
  node.textContent = text;
}

function formatSize(bytes) {
  const units = ['B', 'KB', 'MB', 'GB'];
  let value = Number(bytes) || 0;
  let unit = 0;
  while (value >= 1024 && unit < units.length - 1) {
    value /= 1024;
    unit += 1;
  }
  return (unit === 0 ? value : value.toFixed(1)) + ' ' + units[unit];
}

function paintUpdate(status) {
  const latest = document.getElementById('update-latest');
  const apply = document.getElementById('update-apply');
  if (!latest || !apply) return;
  if (!status) {
    latest.textContent = t('update.idle');
    apply.disabled = true;
    return;
  }
  if (status.error) {
    latest.textContent = t('update.failed', status.error);
    apply.disabled = true;
    return;
  }
  if (!status.available) {
    latest.textContent = t('update.upToDate', status.latest || status.current);
    apply.disabled = true;
    return;
  }
  latest.textContent = t('update.available', status.latest);
  apply.disabled = false;
}

function wireUpdate() {
  const check = document.getElementById('update-check');
  const apply = document.getElementById('update-apply');
  if (!check || !apply) return;

  const runCheck = async (force) => {
    check.disabled = true;
    updateMessage(t('update.checking'));
    try {
      const data = await api('/update' + (force ? '?force=true' : ''));
      paintUpdate(data);
      updateMessage('');
    } catch (error) {
      paintUpdate(null);
      updateMessage(t('update.failed', (error.body && (error.body.message || error.body.code)) || error.message), true);
    } finally {
      check.disabled = false;
    }
  };

  check.addEventListener('click', () => runCheck(true));

  apply.addEventListener('click', async () => {
    apply.disabled = true;
    updateMessage(t('update.applying'));
    try {
      await api('/update', { method: 'POST', body: {} });
      // The host exits so the updater can swap the binaries; reload once it should be back.
      setTimeout(() => location.reload(), 25000);
    } catch (error) {
      updateMessage(t('update.failed', (error.body && (error.body.message || error.body.code)) || error.message), true);
      apply.disabled = false;
    }
  });
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
        <th>${esc(t('accounts.th.ban'))}</th>
        <th>${esc(t('accounts.th.perms'))}</th>
        <th></th>
      </tr></thead>
      <tbody>${rows || '<tr><td colspan="5" class="empty">' + esc(t('accounts.empty')) + '</td></tr>'}</tbody>
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
// player detail
// ---------------------------------------------------------------------------------------------

async function renderPlayer(uid) {
  setActiveTab('accounts');
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
    <h2>UID ${esc(uid)} · ${esc((account && account.username) || '?')}</h2>
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
    // ?lang= lets the server translate the command descriptions itself: the attribute text is shared
    // with the in-game prompt, so it is stored as an I18N key there and resolved per request here.
    const payload = await api('/commands?lang=' + encodeURIComponent(state.lang));
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
            <td class="mono">${esc(cmd.name)}${
              (cmd.aliases || []).length
                ? `<div class="note">${esc(t('console.aliases'))}: ${cmd.aliases
                    .map((alias) => '/' + esc(alias))
                    .join(' ')}</div>`
                : ''
            }</td>
            <td class="mono small">${esc(cmd.usage || '').replace(/\n/g, '<br>')}</td>
            <td class="small">${esc(cmd.description || '').replace(/\n/g, '<br>')}</td>
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
      .map((check) => {
        // The label is a field name; the sentence that says what is wrong comes from the code, in the
        // operator's language. Older servers send no code and only that Chinese sentence in detail.
        const verdict = check.code ? t('setup.check.' + check.key + '.' + check.code, check.detail || '') : '';
        return (
          '<li class="' + (check.ok ? 'ok' : 'bad') + '">' +
          '<span class="pill ' + (check.ok ? 'ok' : 'warn') + '">' + (check.ok ? 'OK' : '!') + '</span> ' +
          '<b>' + esc(t('setup.check.' + check.key)) + '</b> ' +
          '<span class="mono">' + esc(check.detail || '') + '</span>' +
          (verdict && verdict !== check.detail ? '<br><span class="note">' + esc(verdict) + '</span>' : '') +
          '</li>'
        );
      })
      .join('') +
    '</ul>'
  );
}

function setupRegionLabel(id) {
  if (!id) return t('setup.region.none');
  return id === 'cn' ? t('setup.region.cn') : t('setup.region.global');
}

async function renderSetup() {
  const data = await api('/setup');
  setActiveTab('setup');
  state.setupData = data;
  // No 'global' fallback: a config the wizard has never written describes no region at all, and silently
  // preselecting the international build is what made a fresh install look like a deliberate global setup.
  if (!state.setupRegion) state.setupRegion = data.region || null;

  const addresses = (data.addresses && data.addresses.length ? data.addresses : ['127.0.0.1']).slice();
  // The configured address is not necessarily one of the host's own NICs (it can be a public IP the
  // operator typed by hand), so keep it in the list instead of silently defaulting to the first entry.
  if (data.public_address && addresses.indexOf(data.public_address) < 0) {
    addresses.unshift(data.public_address);
  }
  if (!state.setupAddress) {
    state.setupAddress = addresses.indexOf(data.public_address) >= 0 ? data.public_address : addresses[0];
  }

  // A probe run from this page outranks the one /setup returns: that endpoint probes the saved config
  // path, which stays empty until the operator clicks the apply button, so rebuilding from it would wipe
  // a good checklist (and re-lock the apply button) right after a successful check.
  // Only trust the server's probe once a path is actually saved: before that it is the "客户端路径为空"
  // row for a path nobody has entered yet, which reads like an error the moment the wizard opens.
  const savedPath = data.game_path || '';
  const serverProbe = savedPath || data.configured ? data.probe : null;
  const probe = state.setupProbe || serverProbe || { valid: false, checks: [] };
  const region = state.setupRegion;
  const hosts = data.hosts || { entries: [], managed: false };
  // The wizard unlocks in order: channel (and that channel's patch DLL) -> client path -> write config ->
  // launch. Each gate is derived from what the server just reported, never from a local button state, so a
  // stale page cannot offer "启动游戏" for a config that was never written.
  const channelReady = !!region && (region === 'cn' ? !!data.cn_patch_present : !!data.patch_present);
  const pathReady = channelReady && !!probe.valid;
  const configReady = pathReady && !!data.configured;
  const cnMode = state.setupMode || data.cn_launch_mode || 'hosts';
  const locked = (unlocked) => (unlocked ? '' : ' disabled');
  // The CN route is only tested against the old official launcher, so the wizard says so and links the
  // exact build instead of leaving people to hunt for it.
  const cnRegion = (data.regions || []).find((item) => item.id === 'cn');
  const cnLauncher =
    cnRegion && cnRegion.launcher_url
      ? '<p class="note setup-launcher">' +
        esc(t('setup.region.cn.launcher', cnRegion.launcher_version || '')) +
        ' <a href="' + esc(cnRegion.launcher_url) + '" target="_blank" rel="noreferrer noopener">' +
        esc(t('setup.region.cn.download')) +
        '</a></p>'
      : '';

  view.innerHTML = `
    <h2>${esc(t('setup.title'))}</h2>
    <p class="note">${esc(t('setup.lead'))}</p>
    ${probe.valid ? '' : '<div class="callout warn">' + esc(t('setup.pending')) + '</div>'}
    ${region ? '' : '<div class="callout warn">' + esc(t('setup.region.pick')) + '</div>'}

    <h3 class="section"><span class="step-no${channelReady ? ' done' : ''}">1</span>${esc(t('setup.region'))}</h3>
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
    <p id="setup-channel-msg" class="note">${channelReady ? esc(t('setup.channel.ready')) : ''}</p>
    ${region === 'cn' ? `
    <div class="setup-row" id="setup-mode-row">
      <label class="chip"><input type="radio" name="cnmode" value="dll"${cnMode === 'dll' ? ' checked' : ''}> ${esc(t('setup.cnlaunch.dll'))}</label>
      <label class="chip"><input type="radio" name="cnmode" value="hosts"${cnMode === 'hosts' ? ' checked' : ''}> ${esc(t('setup.cnlaunch.hosts'))}</label>
      <span class="note grow">${esc(t('setup.mode.note'))}</span>
    </div>` : ''}
    ${cnLauncher}

    <h3 class="section"><span class="step-no${pathReady ? ' done' : ''}">2</span>${esc(t('setup.path'))}</h3>
    <div class="setup-row">
      <input id="setup-path" class="grow mono" spellcheck="false" placeholder="${esc(t('setup.path.placeholder'))}" value="${esc(state.setupPath ?? data.game_path ?? '')}"${locked(channelReady)}>
      <button id="setup-check" class="secondary"${locked(channelReady)}>${esc(t('setup.path.check'))}</button>
      <button id="setup-detect" class="secondary"${locked(channelReady)}>${esc(t('setup.path.detect'))}</button>
    </div>
    ${channelReady ? '' : '<p class="note">' + esc(t('setup.locked')) + '</p>'}
    <div id="setup-candidates"></div>
    <div id="setup-checks">${setupChecks(probe)}</div>

    <h3 class="section"><span class="step-no${configReady ? ' done' : ''}">3</span>${esc(t('setup.address'))}</h3>
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
      <button id="setup-apply" class="primary"${locked(pathReady)}>${esc(t('setup.apply'))}</button>
      <span id="setup-next" class="note"></span>
    </div>

    ${region === 'cn' && cnMode === 'hosts' ? `
    <h3 class="section">${esc(t('setup.hosts'))}
      <span class="pill ${hosts.entries && hosts.entries.length ? 'ok' : 'warn'}">${
        hosts.entries && hosts.entries.length
          ? esc(t('setup.hosts.on', hosts.entries.length))
          : esc(t('setup.hosts.off'))
      }</span>
    </h3>
    <p class="note">${esc(t('setup.hosts.note'))}${
      data.elevated ? ' ' + esc(t('setup.hosts.elevated')) : ''
    }</p>
    <div class="setup-row">
      <button id="hosts-remove" class="secondary">${esc(t('setup.hosts.remove'))}</button>
      <span id="hosts-msg" class="note grow"></span>
    </div>
    <details class="domains">
      <summary>${esc(t('setup.hosts.domains'))} · ${(data.cn_domains || []).length}</summary>
      <ul class="mono">${(data.cn_domains || []).map((domain) => '<li>' + esc(domain) + '</li>').join('')}</ul>
    </details>
    ` : region === 'cn' ? '<p class="note">' + esc(t('setup.hosts.dllmode')) + '</p>' : ''}

    <h3 class="section"><span class="step-no${configReady ? ' done' : ''}">4</span>${esc(t('setup.launch'))}</h3>
    <p class="note">${esc(region === 'cn' ? (cnMode === 'dll' ? t('setup.launch.dll.note') : t('setup.launch.hosts.note')) : t('setup.launch.note'))}</p>
    <div class="setup-row">
      <button id="setup-launch" class="primary"${locked(configReady)}>${esc(
        region === 'cn' ? (cnMode === 'dll' ? t('setup.cnlaunch.dll') : t('setup.cnlaunch.hosts')) : t('setup.launch')
      )}</button>
      <span id="setup-launch-msg" class="note grow"></span>
    </div>

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
  // The wizard repaints after a probe verdict changes, which rebuilds this input; without keeping the text in
  // state, a path picked from "自动探测" (or typed by hand) vanished right after it was filled in.
  pathInput.addEventListener('input', () => {
    state.setupPath = pathInput.value;
    // The old verdict described the old text: drop it so the checklist cannot claim a path nobody typed.
    state.setupProbe = null;
    checksBox.textContent = '';
  });

  async function probePath() {
    checksBox.innerHTML = '<p class="note">…</p>';
    state.setupPath = pathInput.value;
    try {
      const result = await api('/setup/probe', {
        method: 'POST',
        body: { path: pathInput.value, region: state.setupRegion }
      });
      state.setupProbe = result.probe;
      checksBox.innerHTML = setupChecks(result.probe);
      // A valid probe is what unlocks "写入配置", and that gate is computed during render, so repaint when
      // the verdict changed instead of leaving a disabled button under a green checklist.
      if (!!result.probe.valid !== !!probe.valid) {
        await renderSetup();
      }
    } catch (error) {
      checksBox.innerHTML = '<p class="note">' + esc(error.message) + '</p>';
    }
  }

  // Step one does real work: it asks the server to make sure that channel's patch DLL is on disk (the
  // global build downloads it, the CN build ships it). Only when that answers does the path step unlock,
  // which is why the click awaits the endpoint instead of just repainting the card.
  Array.from(view.querySelectorAll('[data-region]')).forEach((button) => {
    button.addEventListener('click', async () => {
      const next = button.dataset.region;
      state.setupRegion = next;
      state.setupMode = next === 'cn' ? (data.cn_launch_mode || 'hosts') : null;
      await renderSetup();
      const box = document.getElementById('setup-channel-msg');
      if (box) box.textContent = t('setup.channel.preparing');
      try {
        const result = await api('/setup/channel', { method: 'POST', body: { region: next } });
        await renderSetup();
        // The server reports a code now; older builds only have the Chinese detail, which is why the
        // fallback is still there.
        const detail = result.code
          ? t('setup.channel.code.' + result.code, result.patch_path || '')
          : result.detail || '';
        const again = document.getElementById('setup-channel-msg');
        if (again) again.textContent = detail;
        if (!result.ok) toast(detail || t('setup.channel.failed'), true);
      } catch (error) {
        reportError(error);
      }
    });
  });

  Array.from(view.querySelectorAll('input[name="cnmode"]')).forEach((radio) => {
    radio.addEventListener('change', () => {
      state.setupMode = radio.value;
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
          state.setupPath = button.dataset.candidate;
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
    // The server rejects an apply without a region (it cannot pick one for the operator), so say it here
    // rather than letting the button come back with a 400.
    if (!state.setupRegion) {
      toast(t('setup.region.pick'), true);
      return;
    }
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
          public_address: state.setupAddress,
          // Decides the hosts block: "dll" clears it, "hosts" (or global) writes it.
          mode: state.setupRegion === 'cn' ? cnMode : null
        }
      });
      toast(t('setup.done'));
      state.setupRegion = result.region;
      // The server now knows the path, so /setup's own probe is authoritative again.
      state.setupProbe = null;
      await renderSetup();
      const next = document.getElementById('setup-next');
      if (next) {
        const steps = (result.next_step_codes && result.next_step_codes.length
          ? result.next_step_codes.map((code) => t('setup.next.' + code))
          : result.next_steps || []
        )
          .map((step) => esc(step))
          .join('<br>');
        // The wizard writes the hosts block as part of this same button. Report it only when something is
        // off: a success used to be a second button the operator had to find on their own.
        // In DLL mode the hosts block is expected to be gone, so reporting ""hosts has no MikuSB entries""
      // reads like a failure of a step this mode does not run.
      next.innerHTML = steps + (cnMode === 'dll' ? '' : setupHostsReport(result.hosts_sync, data));
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
        // The route follows the config the wizard just wrote, so the button starts whichever way the
        // operator picked in step one instead of asking again.
        const result = await api('/setup/launch', {
          method: 'POST',
          body: { mode: state.setupRegion === 'cn' ? cnMode : null }
        });
        const detail = (result.messages || []).filter(Boolean).join(' ') || result.command;
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
  // Server-raised setup failures arrive as a code plus a Chinese sentence. Prefer the translated code and
  // only fall back to the sentence (or the code in brackets) when this build does not know it yet.
  var code = error && error.body && error.body.code;
  if (code) {
    var key = 'setup.error.' + code;
    var text = t(key);
    if (text !== key) {
      toast(text, true);
      return;
    }
  }
  var extra = code ? ' (' + code + ')' : '';
  toast(((error && error.message) || t('load.failed')) + extra, true);
}

async function route() {
  const hash = location.hash.replace(/^#\/?/, '');
  const parts = hash.split('/').filter(Boolean);
  try {
    if (parts[0] === 'setup') await renderSetup();
    else if (parts[0] === 'accounts') await renderAccounts(parts[1] ? Number(parts[1]) : undefined);
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
  // "Configured" is a region plus a client that passes the probe; a stale region left over from an older
  // install still sends the operator back to the wizard.
  try {
    const setup = await api('/setup');
    if (!setup.configured) {
      if (!location.hash) location.hash = '#/setup';
      state.setupRegion = setup.region || null;
    }
  } catch (error) {
    // A failing probe must not block the panel; the operator can still open Setup manually.
  }
  route();
})();
