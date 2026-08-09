import fs from 'node:fs';
import path from 'node:path';
import process from 'node:process';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const manifestPath = path.join(root, 'eng', 'compatibility.json');
const args = new Set(process.argv.slice(2));
const mode = args.has('--write') ? 'write' : args.has('--check') ? 'check' : null;
if (!mode || args.size !== 1) {
  throw new Error('Usage: node tools/sync-compatibility.mjs --write|--check');
}

const manifest = JSON.parse(fs.readFileSync(manifestPath, 'utf8'));
validateManifest(manifest);

const outputs = new Map([
  [path.join(root, 'eng', 'NinjaSlayer.Compatibility.g.props'), renderProps(manifest)],
  [path.join(root, 'Code', 'Compatibility', 'GameHostContractProfile.g.cs'), renderContracts(manifest)],
  [
    path.join(root, 'tools', 'smoke-harness', 'NinjaSlayer.SmokeDriver', 'NinjaSlayer-SmokeDriver.json'),
    renderSmokeDriverManifest(manifest),
  ],
]);

const documentReplacements = [
  {
    file: path.join(root, 'README.md'),
    start: '<!-- compatibility-badges:start -->',
    end: '<!-- compatibility-badges:end -->',
    content: renderCompatibilityBadges(manifest),
  },
  {
    file: path.join(root, 'README.md'),
    start: '<!-- compatibility:start -->',
    end: '<!-- compatibility:end -->',
    content: renderChineseCompatibility(manifest),
  },
  {
    file: path.join(root, 'README_EN.md'),
    start: '<!-- compatibility-badges:start -->',
    end: '<!-- compatibility-badges:end -->',
    content: renderCompatibilityBadges(manifest),
  },
  {
    file: path.join(root, 'README_EN.md'),
    start: '<!-- compatibility:start -->',
    end: '<!-- compatibility:end -->',
    content: renderEnglishCompatibility(manifest),
  },
  {
    file: path.join(root, 'Docs', 'development.md'),
    start: '<!-- compatibility:start -->',
    end: '<!-- compatibility:end -->',
    content: renderDevelopmentCompatibility(manifest),
  },
];

for (const replacement of documentReplacements) {
  const source = outputs.get(replacement.file) ?? fs.readFileSync(replacement.file, 'utf8');
  outputs.set(replacement.file, replaceMarkedSection(source, replacement));
}

const drift = [];
for (const [file, expected] of outputs) {
  const normalized = normalize(expected);
  const actual = fs.existsSync(file) ? normalize(fs.readFileSync(file, 'utf8')) : null;
  if (actual === normalized) continue;
  if (mode === 'check') {
    drift.push(path.relative(root, file).replaceAll('\\', '/'));
    continue;
  }
  fs.mkdirSync(path.dirname(file), { recursive: true });
  fs.writeFileSync(file, normalized, 'utf8');
}

if (drift.length > 0) {
  throw new Error(`Compatibility-derived files are stale: ${drift.join(', ')}. Run node tools/sync-compatibility.mjs --write.`);
}

console.log(mode === 'check' ? 'Compatibility-derived files are current.' : 'Compatibility-derived files updated.');

function validateManifest(value) {
  if (value.schemaVersion !== 2) throw new Error('compatibility.json schemaVersion must be 2.');
  if (!/^\d+\.\d+\.\d+$/.test(value.ritsuLibVersion ?? '')) {
    throw new Error('ritsuLibVersion must be an exact SemVer core.');
  }
  if (!/^\d+$/.test(value.workshop?.itemId ?? '')
      || !['private', 'unlisted'].includes(value.workshop?.visibility)) {
    throw new Error('workshop must declare one numeric private or unlisted item.');
  }
  const spineFiles = value.spineExtension?.windowsFiles;
  if (!Array.isArray(spineFiles) || spineFiles.length !== 3) {
    throw new Error('spineExtension.windowsFiles must contain exactly three files.');
  }
  const spineNames = new Set();
  for (const file of spineFiles) {
    if (!/^libspine_godot\.windows\.[A-Za-z0-9_.-]+\.dll$/.test(file?.name ?? '')
        || !/^[0-9a-f]{64}$/.test(file?.sha256 ?? '')
        || spineNames.has(file.name)) {
      throw new Error('spineExtension.windowsFiles contains an invalid or duplicate file.');
    }
    spineNames.add(file.name);
  }
  const names = Object.keys(value.channels ?? {});
  if (names.join(',') !== 'stable,preview') {
    throw new Error('compatibility.json must declare stable and preview, in that order.');
  }
  if (!names.includes(value.defaultBuildChannel)) {
    throw new Error('defaultBuildChannel must name an active channel.');
  }
  if (value.defaultBuildChannel !== 'preview') {
    throw new Error('defaultBuildChannel must be preview.');
  }
  for (const name of names) {
    const channel = value.channels[name];
    if (!/^\d+\.\d+\.\d+$/.test(channel.gameApiVersion ?? '')) {
      throw new Error(`${name}.gameApiVersion must be an exact version.`);
    }
    if (!/^STS2\.RitsuLib(?:\.Compat\.\d+\.\d+\.\d+)?$/.test(channel.ritsuLibPackageId ?? '')) {
      throw new Error(`${name}.ritsuLibPackageId is invalid.`);
    }
    const expectedPackage = name === 'stable'
      ? `STS2.RitsuLib.Compat.${channel.gameApiVersion}`
      : 'STS2.RitsuLib';
    if (channel.ritsuLibPackageId !== expectedPackage) {
      throw new Error(`${name}.ritsuLibPackageId must be ${expectedPackage}.`);
    }
    if (channel.distributionChannel !== (name === 'stable' ? 'public' : 'beta')) {
      throw new Error(`${name}.distributionChannel is invalid.`);
    }
    if (!Array.isArray(channel.runtimeAssemblies) || !Array.isArray(channel.compileFeatures)) {
      throw new Error(`${name} runtimeAssemblies and compileFeatures must be arrays.`);
    }
    if (new Set(channel.runtimeAssemblies).size !== channel.runtimeAssemblies.length
        || channel.runtimeAssemblies.some(file => !/^[A-Za-z0-9_.-]+\.dll$/.test(file))) {
      throw new Error(`${name}.runtimeAssemblies contains an invalid or duplicate file.`);
    }
    if (new Set(channel.compileFeatures).size !== channel.compileFeatures.length) {
      throw new Error(`${name}.compileFeatures contains duplicates.`);
    }
    const allowedFeatures = new Set([
      'legacyCardPlayLinks',
      'legacyCreaturePresentation',
      'legacyDamageApi',
      'legacyArchitectVictoryCompletion',
    ]);
    for (const feature of channel.compileFeatures) {
      if (!allowedFeatures.has(feature)) throw new Error(`${name} has unknown compile feature ${feature}.`);
    }
    validateHostContract(name, channel.hostContract);
  }
}

function validateHostContract(name, contract) {
  if (!contract || !/^\d+\.\d+\.\d+\.\d+$/.test(contract.assemblyVersion ?? '')) {
    throw new Error(`${name}.hostContract.assemblyVersion is invalid.`);
  }
  if (typeof contract.buildVariant !== 'string' || contract.buildVariant.length === 0) {
    throw new Error(`${name}.hostContract.buildVariant is required.`);
  }
  if (!/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(contract.moduleMvid ?? '')) {
    throw new Error(`${name}.hostContract.moduleMvid is invalid.`);
  }
  for (const [key, method] of [
    ['lethalDamage', contract.lethalDamage],
    ['preparedDraw.publicMethod', contract.preparedDraw?.publicMethod],
    ['preparedDraw.asyncMoveNext', contract.preparedDraw?.asyncMoveNext],
    ['preparedQueueAdd', contract.preparedQueueAdd],
    ['preparedQueueRemove', contract.preparedQueueRemove],
  ]) validateMethod(name, key, method);
  if (!['DirectAsync', 'WrapperWithAsyncInternal'].includes(contract.preparedDraw?.layout)) {
    throw new Error(`${name}.hostContract.preparedDraw.layout is invalid.`);
  }
  if (contract.preparedDraw.layout === 'DirectAsync' && contract.preparedDraw.internalMethod !== null) {
    throw new Error(`${name} direct async draw must not have an internal method.`);
  }
  if (contract.preparedDraw.layout === 'WrapperWithAsyncInternal') {
    validateMethod(name, 'preparedDraw.internalMethod', contract.preparedDraw.internalMethod);
  }
  if (!Array.isArray(contract.sensitiveMethods)) {
    throw new Error(`${name}.hostContract.sensitiveMethods must be an array.`);
  }
  const ids = new Set();
  for (const method of contract.sensitiveMethods) {
    if (!method.id || !method.signature || ids.has(method.id)) {
      throw new Error(`${name} sensitive method needs a unique id and signature.`);
    }
    ids.add(method.id);
    if (!['Method', 'AsyncMoveNext'].includes(method.capture)) {
      throw new Error(`${name}.${method.id}.capture is invalid.`);
    }
    if (method.capture === 'AsyncMoveNext' && !method.stateMachineType) {
      throw new Error(`${name}.${method.id} must record its async state-machine type.`);
    }
    validateMethod(name, `sensitiveMethods.${method.id}`, method);
  }
}

function validateMethod(channel, name, method) {
  if (!method || !/^0x[0-9A-Fa-f]{8}$/.test(method.metadataToken ?? '') || !/^[0-9a-f]{64}$/.test(method.ilSha256 ?? '')) {
    throw new Error(`${channel}.hostContract.${name} has an invalid token or IL hash.`);
  }
}

function renderProps(value) {
  const featureSymbols = {
    legacyCardPlayLinks: 'NINJASLAYER_LEGACY_CARD_PLAY_LINKS',
    legacyCreaturePresentation: 'NINJASLAYER_LEGACY_CREATURE_PRESENTATION',
    legacyDamageApi: 'NINJASLAYER_LEGACY_DAMAGE_API',
    legacyArchitectVictoryCompletion: 'NINJASLAYER_LEGACY_ARCHITECT_VICTORY_COMPLETION',
  };
  const groups = Object.entries(value.channels).map(([name, channel]) => {
    const symbols = [channelSymbol(name), ...channel.compileFeatures.map(feature => featureSymbols[feature])];
    return `  <PropertyGroup Condition="'$(NinjaSlayerHostChannel)' == '${name}'">\n` +
      `    <NinjaSlayerGameApiVersion>${channel.gameApiVersion}</NinjaSlayerGameApiVersion>\n` +
      `    <NinjaSlayerRitsuLibPackageId>${xml(channel.ritsuLibPackageId)}</NinjaSlayerRitsuLibPackageId>\n` +
      `    <NinjaSlayerManifestMinGameVersion>${channel.gameApiVersion}</NinjaSlayerManifestMinGameVersion>\n` +
      `    <NinjaSlayerDistributionChannel>${channel.distributionChannel}</NinjaSlayerDistributionChannel>\n` +
      `    <NinjaSlayerHostModuleMvid>${channel.hostContract.moduleMvid}</NinjaSlayerHostModuleMvid>\n` +
      `    <NinjaSlayerRuntimeAssemblies>${channel.runtimeAssemblies.join(';')}</NinjaSlayerRuntimeAssemblies>\n` +
      `    <DefineConstants>$(DefineConstants);${symbols.join(';')}</DefineConstants>\n` +
      '  </PropertyGroup>';
  }).join('\n\n');
  return `<!-- Generated by tools/sync-compatibility.mjs. Do not edit. -->
<Project>
  <PropertyGroup>
    <NinjaSlayerHostChannelWasExplicit Condition="'$(NinjaSlayerHostChannel)' != ''">true</NinjaSlayerHostChannelWasExplicit>
    <NinjaSlayerHostChannel Condition="'$(NinjaSlayerHostChannel)' == ''">${value.defaultBuildChannel}</NinjaSlayerHostChannel>
    <NinjaSlayerRitsuLibVersion>${value.ritsuLibVersion}</NinjaSlayerRitsuLibVersion>
    <NinjaSlayerWorkshopItemId>${value.workshop.itemId}</NinjaSlayerWorkshopItemId>
    <NinjaSlayerWorkshopVisibility>${value.workshop.visibility}</NinjaSlayerWorkshopVisibility>
    <NinjaSlayerCompatibilityManifest>$(MSBuildThisFileDirectory)compatibility.json</NinjaSlayerCompatibilityManifest>
  </PropertyGroup>

${groups}

  <Target Name="ValidateNinjaSlayerHostChannel" BeforeTargets="PrepareForBuild">
    <Error Condition="'$(NinjaSlayerHostChannel)' != 'stable' and '$(NinjaSlayerHostChannel)' != 'preview'"
           Text="NinjaSlayerHostChannel must be stable or preview, not '$(NinjaSlayerHostChannel)'." />
    <Error Condition="'$(NinjaSlayerGameApiVersion)' == '' or '$(NinjaSlayerRitsuLibPackageId)' == ''"
           Text="NinjaSlayerHostChannel '$(NinjaSlayerHostChannel)' did not resolve a compatibility profile." />
  </Target>
</Project>
`;
}

function renderContracts(value) {
  const entries = Object.entries(value.channels)
    .map(([name, channel]) => renderContract(name, channel, value.ritsuLibVersion))
    .join(',\n');
  const current = Object.keys(value.channels).map((name, index) =>
    `${index === 0 ? '#if' : '#elif'} ${channelSymbol(name)}\n        AllProfiles[${index}]`
  ).join('\n') + '\n#else\n#error NinjaSlayer host channel symbol was not generated.\n#endif';
  return `// Generated by tools/sync-compatibility.mjs. Do not edit.
namespace NinjaSlayer.Code.Compatibility;

internal static class GeneratedGameHostContracts
{
    private static readonly GameHostContractProfile[] AllProfiles =
    [
${entries}
    ];

    public static IReadOnlyList<GameHostContractProfile> All { get; } =
        Array.AsReadOnly(AllProfiles);

    public static IReadOnlyList<GameHostContractProfile> Current { get; } =
        Array.AsReadOnly<GameHostContractProfile>(
        [
${current}
        ]);
}
`;
}

function renderContract(name, channel, ritsuLibVersion) {
  const contract = channel.hostContract;
  const draw = contract.preparedDraw;
  const internalMethod = draw.internalMethod ? renderMethod(draw.internalMethod) : 'null';
  return `        new(
            "${name}",
            "${channel.gameApiVersion}",
            "${channel.ritsuLibPackageId}",
            "${ritsuLibVersion}",
            "${contract.buildVariant}",
            "${contract.assemblyVersion}",
            Guid.Parse("${contract.moduleMvid}"),
            ${renderMethod(contract.lethalDamage)},
            new(
                PreparedDrawHostLayout.${draw.layout},
                ${renderMethod(draw.publicMethod)},
                ${internalMethod},
                ${renderMethod(draw.asyncMoveNext)}),
            ${renderMethod(contract.preparedQueueAdd)},
            ${renderMethod(contract.preparedQueueRemove)})`;
}

function renderMethod(method) {
  return `new(${method.metadataToken}, "${method.ilSha256}")`;
}

function channelSymbol(name) {
  return `NINJASLAYER_CHANNEL_${name.toUpperCase()}`;
}

function renderCompatibilityBadges(value) {
  const stable = value.channels.stable.gameApiVersion;
  const preview = value.channels.preview.gameApiVersion;
  return `<!-- compatibility-badges:start -->
  <p>
    <img src="https://img.shields.io/badge/C%23-.NET-512BD4?logo=dotnet&amp;logoColor=white" alt="C#">
    <img src="https://img.shields.io/badge/.NET-9.0-512BD4?logo=dotnet&amp;logoColor=white" alt=".NET 9">
    <img src="https://img.shields.io/badge/Godot-4.5.1-478CBF?logo=godotengine&amp;logoColor=white" alt="Godot 4.5.1">
    <img src="https://img.shields.io/badge/Slay%20the%20Spire%202-${stable}%20%7C%20${preview}-B51F24" alt="Slay the Spire 2 ${stable} and ${preview}">
    <img src="https://img.shields.io/badge/RitsuLib-${value.ritsuLibVersion}-2D7D9A" alt="RitsuLib ${value.ritsuLibVersion}">
    <a href="https://github.com/2223M1/NinjaSlayer/releases/latest"><img src="https://img.shields.io/github/v/release/2223M1/NinjaSlayer?display_name=tag&amp;sort=semver" alt="GitHub Release"></a>
  </p>
  <!-- compatibility-badges:end -->`;
}

function renderSmokeDriverManifest(value) {
  return `${JSON.stringify({
    id: 'NinjaSlayer-SmokeDriver',
    name: 'NinjaSlayer Smoke Driver',
    author: 'NinjaSlayer maintainers',
    description: 'Protected real-game smoke test driver. Never distribute with the mod.',
    version: '1.0.0',
    min_game_version: value.channels.stable.gameApiVersion,
    has_pck: false,
    has_dll: true,
    dependencies: [
      { id: 'STS2-RitsuLib', min_version: value.ritsuLibVersion },
      { id: 'NinjaSlayer', min_version: '0.0.0' },
    ],
    affects_gameplay: true,
  }, null, 2)}\n`;
}

function renderChineseCompatibility(value) {
  const stable = value.channels.stable;
  const preview = value.channels.preview;
  return `<!-- compatibility:start -->
| 组件 | 支持范围 |
|---|---|
| Slay the Spire 2 | stable 正式版 \`${stable.gameApiVersion}\`；preview 测试版 \`${preview.gameApiVersion}\` |
| RitsuLib | 编译基线与最低依赖 \`${value.ritsuLibVersion}\`；Workshop 运行时使用自动更新的最新版 |
| 平台目标 | Windows x64、macOS、Linux x86_64 / Steam Deck；正式跨平台支持须通过六格实机矩阵 |
| .NET | \`9.0\` |
| Godot | \`4.5.1 Mono\` |
| 游戏内语言 | 目前主要提供简体中文 |

GitHub Release 提供 stable、preview 两个宿主专用诊断包和一个通用 Workshop 包。Workshop 条目不进入公开列表和搜索，但可通过链接访问并订阅；所有玩家下载同一个包，启动时按游戏宿主精确选择 stable 或 preview 实现。通用 PCK 不携带 Spine 原生扩展，运行时复用官方客户端已经加载的当前平台扩展。当前自动化真实游戏测试仅覆盖 Windows；macOS 与 Linux 的 stable/preview 实机矩阵通过前不宣称已完成正式跨平台验证。
<!-- compatibility:end -->`;
}

function renderEnglishCompatibility(value) {
  const stable = value.channels.stable;
  const preview = value.channels.preview;
  return `<!-- compatibility:start -->
| Component | Supported version |
|---|---|
| Slay the Spire 2 | stable public \`${stable.gameApiVersion}\`; preview beta \`${preview.gameApiVersion}\` |
| RitsuLib | build baseline and minimum dependency \`${value.ritsuLibVersion}\`; Workshop installs receive its current release automatically |
| Platform targets | Windows x64, macOS, and Linux x86_64 / Steam Deck; formal cross-platform support requires the six-cell real-device matrix |
| .NET | \`9.0\` |
| Godot | \`4.5.1 Mono\` |
| In-game language | Primarily Simplified Chinese at present |

Each GitHub Release contains stable and preview diagnostic archives plus one universal Workshop archive. The unlisted Workshop item remains accessible by link; every subscriber receives the same bundle, which selects the exact stable or preview implementation at startup. The universal PCK carries no native Spine extension and reuses the platform extension already loaded by the official client. Automated real-game testing currently covers Windows only; formal macOS and Linux support is not claimed until both channels pass on those platforms.
<!-- compatibility:end -->`;
}

function renderDevelopmentCompatibility(value) {
  return `<!-- compatibility:start -->
| Channel | Game API | RitsuLib compile package | Distribution |
|---|---|---|---|
${Object.entries(value.channels).map(([name, channel]) =>
    `| \`${name}\` | \`${channel.gameApiVersion}\` | \`${channel.ritsuLibPackageId} ${value.ritsuLibVersion}\` | \`${channel.distributionChannel}\` |`
  ).join('\n')}

Only these two rolling channels are active. Runtime players receive the current RitsuLib Workshop build; \`${value.ritsuLibVersion}\` is the pinned, reproducible compile baseline and minimum manifest dependency. Protected builds use each channel's real game assemblies. No intermediate game host or RefLib approximation is a release target.
<!-- compatibility:end -->`;
}

function replaceMarkedSection(source, replacement) {
  const start = source.indexOf(replacement.start);
  const end = source.indexOf(replacement.end);
  if (start < 0 || end < start) {
    throw new Error(`${path.relative(root, replacement.file)} is missing compatibility markers.`);
  }
  return source.slice(0, start) + replacement.content + source.slice(end + replacement.end.length);
}

function normalize(value) {
  return value.replaceAll('\r\n', '\n').replace(/[ \t]+$/gm, '').replace(/\s*$/, '\n');
}

function xml(value) {
  return value.replaceAll('&', '&amp;').replaceAll('<', '&lt;').replaceAll('>', '&gt;');
}
