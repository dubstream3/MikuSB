// Runner for the EraAudit Lua harness. Uses the pure-JS Lua 5.3 VM (fengari) so the emitted
// payloads can be parsed and executed without a native Lua install.
//
//   node tests/EraAudit/lua/run.js [--out <dir>] [--fengari <node_modules dir>]
//
// Exit code 0 = all assertions passed, 1 = failures (printed by run.lua), 2 = harness error.
const path = require('path');

let fengariPath = process.env.ERA_FENGARI || null;
const outIdx = process.argv.indexOf('--out');
const out = outIdx >= 0 ? process.argv[outIdx + 1] : path.join(__dirname, '..', 'out');
const fengariIdx = process.argv.indexOf('--fengari');
if (fengariIdx >= 0) fengariPath = process.argv[fengariIdx + 1];

const fengari = require(fengariPath ? path.join(fengariPath, 'fengari') : 'fengari');
const { lua, lauxlib, lualib, to_luastring } = fengari;

const L = lauxlib.luaL_newstate();
lualib.luaL_openlibs(L);

lua.lua_pushstring(L, to_luastring(path.resolve(out)));
lua.lua_setglobal(L, to_luastring('ERA_OUT_DIR'));

const status = lauxlib.luaL_dofile(L, to_luastring(path.join(__dirname, 'run.lua')));
if (status !== lua.LUA_OK) {
  const message = lua.lua_tojsstring(L, -1);
  console.error(`EraAudit(Lua): harness error: ${message}`);
  process.exit(2);
}

lua.lua_getglobal(L, to_luastring('ERA_AUDIT_FAILED'));
const failed = lua.lua_tointeger(L, -1);
process.exit(failed === 0 ? 0 : 1);
