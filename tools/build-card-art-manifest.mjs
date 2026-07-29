import { existsSync, mkdirSync, readFileSync, readdirSync, statSync, writeFileSync } from 'node:fs';
import { dirname, join, relative, resolve } from 'node:path';

const repoRoot = resolve(import.meta.dirname, '..');

function option(name) {
  const index = process.argv.indexOf(`--${name}`);
  if (index < 0 || !process.argv[index + 1]) {
    throw new Error(`Missing --${name}`);
  }
  return resolve(process.argv[index + 1]);
}

const actionRoot = option('action-root');
const characterReference = option('character-ref');
const styleRoot = option('style-root');
const outputPath = option('output');
const generatedRoot = resolve(dirname(outputPath), 'generated');

const palettes = {
  redCyan: 'scarlet red and saturated cyan/teal, each occupying a substantial area; mustard-yellow only as a small impact accent',
  violetYellow: 'deep violet and bright acid yellow as the two dominant hue families, with restrained scarlet on Ninja Slayer',
  tealAmber: 'turquoise/teal and warm amber-orange as the two dominant hue families, separated primarily by direct hue and value boundaries; charcoal is reserved for naturally dark costume or mask shapes',
  cobaltCoral: 'clear cobalt blue and hot coral-red as the two dominant hue families, with a small pale-gold accent',
  magentaLime: 'saturated magenta/violet and sharp lime-green as the two dominant hue families, with restrained red-black character colors',
  indigoOrange: 'deep indigo/cyan-blue and vivid orange as the two dominant hue families, with dark red used only on the costume',
  purpleGold: 'dark purple and luminous gold as the two dominant hue families, with a compact red-black focal subject',
  crimsonBlue: 'crimson red and cool blue-cyan as the two dominant hue families, with small off-white highlights',
  narakuPurpleBlack: 'scarlet costume red and saturated violet-purple as the two dominant hue families, with Naraku represented only by deep purple, violet, or matte black',
};

const designs = {
  ShurikenStock: ['A close forearm guard hides two large shuriken beneath one lifted sleeve; the opposite hand forms a compact defensive angle.', '0ef677bd68dcd788e283f53b46aced97', 'silent/cloak_and_dagger.png', 'redCyan', 'character'],
  ThrowKunai: ['A single kunai leaves Ninja Slayer\'s hand on a clean diagonal while one narrowed eye reads the next opening; no extra projectiles.', '6c8be1f863e4a86143ddbd479458972c', 'silent/dagger_throw.png', 'violetYellow', 'character'],
  ShurikenThrow: ['One large shuriken crosses the foreground toward a simple stone target while Ninja Slayer finishes the throw in the background.', '70605e0b50ff2e53c6a9a06c84f21242', 'silent/dagger_throw.png', 'cobaltCoral', 'character'],
  ShurikenSpread: ['One gloved hand fans exactly two large shuriken toward the viewer; the masked face is cropped behind the hand.', '0ef677bd68dcd788e283f53b46aced97', 'silent/fan_of_knives.png', 'violetYellow', 'character'],
  NinjaSlayerBladeDance: ['Ninja Slayer turns through one sweeping throw; exactly three large shuriken and one broad yellow motion fan imply the dance.', '8aa974717dbf9b98b9f2846cf510a718', 'silent/blade_dance.png', 'violetYellow', 'character'],
  ShurikenGuard: ['Two crossed shuriken intercept one incoming orange strike in front of Ninja Slayer\'s guarded torso.', '9ca79bbafca0c83d90eed899ab658588', 'ironclad/defend_ironclad.png', 'tealAmber', 'character'],
  IyaIronSlashWave: ['Exactly three shuriken align into a broad steel wave that becomes a cyan shield in front of one planted arm.', '9ca79bbafca0c83d90eed899ab658588', 'silent/storm_of_steel.png', 'indigoOrange', 'character'],
  ReadyBlade: ['A poised hand holds exactly three large shuriken in a clean fan while the other hand waits near the shoulder.', '0968cbce96c6a749b68b2347d1b3b479', 'silent/prepared.png', 'magentaLime', 'character'],
  ShurikenCleave: ['Ninja Slayer makes one wide airborne throw; exactly three large shuriken cross separate paths and one broad yellow arc implies an all-enemy sweep.', '0226058b7c7f9ef60cb3c4b6de19955c', 'silent/blade_dance.png', 'violetYellow', 'character'],
  RubHands: ['A sleeve snaps open to reveal one hidden shuriken while the opposite fist completes a previous attack.', '70605e0b50ff2e53c6a9a06c84f21242', 'silent/cloak_and_dagger.png', 'redCyan', 'character'],
  StarlessNight: ['Three small shuriken dissolve into one large black-steel shuriken against a simple starless indigo void.', '6e6df5c752da3121e56957c24cfa19ab', 'silent/infinite_blades.png', 'purpleGold', 'object'],
  Contraption: ['A compact black-metal wrist launcher presents exactly three empty shuriken slots and one loaded blade.', '167dd11447efa51ddf9c4e608cb4c7a7', 'colorless/secret_weapon.png', 'tealAmber', 'partial'],
  BladesCome: ['Exactly three large shuriken emerge from a dark violet plane toward one beckoning red-black hand.', '2d09c6319656914dc18bce0530aeb6ea', 'silent/infinite_blades.png', 'magentaLime', 'partial'],
  TeaDrinkingSword: ['One shuriken crosses a single spiral of pale tea steam above an amber tea bowl, joining blade discipline with Chado.', '01d3fa531d0a1dcf4bd89096cf2b93d0', 'ironclad/battle_trance.png', 'tealAmber', 'object'],
  HellTornado: ['Ninja Slayer rises inside one broad tornado ribbon with exactly three large shuriken carried around its rim.', '36929cf7169200082b714eee79963962', 'ironclad/whirlwind.png', 'indigoOrange', 'character'],
  OmnidirectionalThrow: ['Four large shuriken launch in four clear directions from one compact central throwing pose; no smaller repeated blades.', '0226058b7c7f9ef60cb3c4b6de19955c', 'silent/fan_of_knives.png', 'violetYellow', 'character'],
  Injection: ['One shuriken bites into a turquoise training target and sends one red pulse line through the impact point.', 'c608acc3b0b68b0a1a5c2d6b269f7b52', 'silent/assassinate.png', 'redCyan', 'object'],
  GiantShurikenCard: ['A single enormous four-point shuriken fills the image against two broad red and yellow speed planes.', '6e6df5c752da3121e56957c24cfa19ab', 'silent/fan_of_knives.png', 'cobaltCoral', 'object'],
  ShurikenCard: ['A single four-point shuriken flies diagonally across one violet motion wedge; no character and no additional blades.', '0ef677bd68dcd788e283f53b46aced97', 'silent/dagger_throw.png', 'violetYellow', 'object'],

  Meditation: ['A cropped seated Ninja Slayer steadies both hands above one tea bowl while two broad cyan breath bands clear a red-black haze.', '01d3fa531d0a1dcf4bd89096cf2b93d0', 'colorless/calm.png', 'crimsonBlue', 'character'],
  SipTea: ['Both gloved hands lift one small amber tea bowl toward the dark menpo while one cyan steam ribbon clears the eye line.', 'dea82e5c259a225cd53b10a666d7d1b9', 'colorless/deep_breath.png', 'tealAmber', 'character'],
  RestGuard: ['Ninja Slayer sits in a compact guard with one tea bowl protected behind a single curved cyan shield plane.', '9ca79bbafca0c83d90eed899ab658588', 'ironclad/defend_ironclad.png', 'redCyan', 'character'],
  TeaHitsPeople: ['One turquoise tea wave sweeps from an amber bowl across exactly two simple red target silhouettes.', 'f5fa5e0a8def33eb680da9c3693eb798', 'colorless/shockwave.png', 'tealAmber', 'partial'],
  SteepTea: ['An amber tea bowl, a simple whisk, and one curl of turquoise steam form a quiet object still life.', 'dea82e5c259a225cd53b10a666d7d1b9', 'colorless/equilibrium.png', 'tealAmber', 'object'],
  WhiskSlash: ['One bamboo tea whisk becomes a single bright diagonal slash from Ninja Slayer\'s cropped hand.', 'cbb1dcac77249306f8eed2d697458c80', 'silent/cloak_and_dagger.png', 'tealAmber', 'partial'],
  ImpureFlame: ['A black-red flame burns beneath one turquoise kettle and changes into one clean pale steam plume.', 'b12fef5455f0a447e16f8103140ddafe', 'ironclad/burning_pact.png', 'redCyan', 'object'],
  ColdBrew: ['One chilled blue tea bowl rests in a dark indigo field while amber leaves and one crisp steam curl show regained clarity.', '01d3fa531d0a1dcf4bd89096cf2b93d0', 'colorless/calm.png', 'tealAmber', 'object'],
  DrinkTea: ['One fist grips an amber bowl as a broad turquoise vigor halo rises behind the cropped masked head.', '7a3c1fe7021752c1a051965afc7ae969', 'ironclad/battle_trance.png', 'tealAmber', 'character'],
  PourTea: ['A tilted amber bowl pours one turquoise stream that curves into a protective shield around a red-black forearm.', 'dea82e5c259a225cd53b10a666d7d1b9', 'ironclad/defend_ironclad.png', 'tealAmber', 'partial'],
  AssassinationFist: ['One dark fist emerges through two broad turquoise tea-steam rings toward a simple amber target.', '40182d846ee4e4529c3f3eea85c2d51b', 'ironclad/molten_fist.png', 'tealAmber', 'character'],
  TeaOffering: ['Two gloved hands offer one amber tea bowl in front of a single turquoise shield disc.', '7a3c1fe7021752c1a051965afc7ae969', 'ironclad/defend_ironclad.png', 'tealAmber', 'partial'],
  BrewTea: ['One black tea leaf falls into an amber bowl and releases a broad turquoise steam shape, suggesting deliberate sacrifice.', 'dea82e5c259a225cd53b10a666d7d1b9', 'ironclad/burning_pact.png', 'tealAmber', 'object'],
  GreatUke: ['Ninja Slayer turns through one controlled fall while a broad turquoise tea-steam cushion redirects an orange impact.', 'f99b2b80b5cefd7c854ecfa0c8452935', 'silent/backflip.png', 'indigoOrange', 'character'],
  ClankDrinkTea: ['The cropped masked head drinks from one metal bowl as one bold cyan-and-gold energy burst restores alertness.', 'dea82e5c259a225cd53b10a666d7d1b9', 'colorless/deep_breath.png', 'tealAmber', 'character'],
  DrowsyBlackTea: ['One black tea bowl sinks into a violet sleep haze while exactly three pale geometric forms soften around it.', 'fd8c247e513f32aeaabaf4dd8ef9df00', 'colorless/forethought.png', 'purpleGold', 'object'],
  BeatPeopleChado: ['A seated Ninja Slayer remains still inside one cyan breath ring while one large red fist shadow waits behind him.', '01d3fa531d0a1dcf4bd89096cf2b93d0', 'ironclad/battle_trance.png', 'redCyan', 'character'],
  SenchaStorm: ['One enormous turquoise tea-and-wind spiral sweeps across exactly two simple orange target shapes.', '36929cf7169200082b714eee79963962', 'colorless/shockwave.png', 'tealAmber', 'partial'],
  TeaShield: ['One amber tea bowl sits at the center of a large turquoise shield disc absorbing exactly two red cracks.', '9ca79bbafca0c83d90eed899ab658588', 'ironclad/barricade.png', 'tealAmber', 'object'],
  TeaSamadhi: ['A seated Ninja Slayer, one tea bowl, and one complete cyan breath halo create a stable repeating ritual.', '7a3c1fe7021752c1a051965afc7ae969', 'colorless/calm.png', 'crimsonBlue', 'character'],
  ZazenDrink: ['A vertically framed seated Ninja Slayer holds one large amber vessel while a single turquoise steam pillar rises above him.', '7a3c1fe7021752c1a051965afc7ae969', 'ironclad/break.png', 'tealAmber', 'character'],
  ChadoCard: ['One amber tea bowl and three broad turquoise breath rings form a simple status-card symbol; no character.', 'dea82e5c259a225cd53b10a666d7d1b9', 'colorless/equilibrium.png', 'tealAmber', 'object'],

  KarateStraight: ['A cropped Ninja Slayer drives one straight horizontal punch into a large turquoise stone guard; one red motion band and one amber impact burst.', '681081a17038a21aab78493add46ff8b', 'ironclad/anger.png', 'redCyan', 'character'],
  KataDrill: ['Ninja Slayer holds one precise stance before a wooden post while two broad pose arcs show disciplined repetition without duplicate bodies.', '0968cbce96c6a749b68b2347d1b3b479', 'ironclad/one_two_punch.png', 'cobaltCoral', 'character'],
  NinjaWall: ['A planted torso braces behind crossed forearms and one massive cyan slab while an orange strike breaks around it.', '9ca79bbafca0c83d90eed899ab658588', 'ironclad/barricade.png', 'indigoOrange', 'character'],
  Chop: ['One open red-black hand chops diagonally through a single turquoise practice board.', 'e8d4d224399b47ae651e66af132fc585', 'colorless/secret_weapon.png', 'redCyan', 'partial'],
  PerfectChop: ['One perfectly aligned open-hand chop divides a round blue stone into exactly two clean halves.', 'e8d4d224399b47ae651e66af132fc585', 'silent/neutralize.png', 'cobaltCoral', 'partial'],
  PalmThrust: ['One large open palm dominates the foreground and releases two broad cyan impact rings toward an orange target.', 'ae219dce2eb346da2e95111643734898', 'colorless/shockwave.png', 'tealAmber', 'character'],
  HalfMoonCompassKick: ['One crescent kick cuts a clean turquoise half-moon arc through an amber target; one tea-steam ribbon trails the leg.', '3864b3fe0f40fc97a0e0bc6fd3c58182', 'ironclad/whirlwind.png', 'tealAmber', 'character'],
  BackBridge: ['Ninja Slayer arches backward beneath one orange attack while red and cyan burden marks peel away into a gold energy arc.', 'f0b419d39f1c996a3ae9cc5ec2ecf74f', 'silent/backflip.png', 'cobaltCoral', 'character'],
  SpitWater: ['A cropped dark menpo expels one sharp turquoise spray through its vents toward a simple purple target silhouette.', 'f5fa5e0a8def33eb680da9c3693eb798', 'colorless/shockwave.png', 'magentaLime', 'character'],
  SweepKick: ['One low sweeping leg crosses the image and knocks exactly three simple turquoise training blocks outward.', 'd7cf55f818ce8584a1c68553ee5451b9', 'ironclad/whirlwind.png', 'redCyan', 'partial'],
  ShieldFromNothing: ['Two empty guarded hands define one large cyan shield plane as a single orange blow bends around it.', '9ca79bbafca0c83d90eed899ab658588', 'ironclad/defend_ironclad.png', 'indigoOrange', 'partial'],
  MurderFist: ['One clenched black-red fist hangs above a split purple target silhouette, lit by one hard yellow execution wedge.', '4691f6d0e5f8c6c30f9798855de19ce4', 'silent/finisher.png', 'violetYellow', 'partial'],
  StraightKi: ['Two gloved fingertips deliver one precise cyan-red pulse into the chest point of a simple amber training dummy.', 'ae219dce2eb346da2e95111643734898', 'silent/neutralize.png', 'tealAmber', 'partial'],
  IronShirt: ['A cropped armored torso glows turquoise and gold while exactly three red strike wedges bounce away.', '9ca79bbafca0c83d90eed899ab658588', 'ironclad/impervious.png', 'tealAmber', 'partial'],
  Redouble: ['One fist is followed by a single cyan afterimage and one red afterimage, expressing doubled Karate without extra limbs.', '40182d846ee4e4529c3f3eea85c2d51b', 'ironclad/twin_strike.png', 'redCyan', 'partial'],
  StunStrike: ['One heavy upward fist rings a large turquoise bronze-edged sphere with one yellow stun burst.', '39159d935cb7e10ecc7c1585cb024076', 'ironclad/bash.png', 'tealAmber', 'character'],
  BangBangFist: ['Exactly two anatomically connected fists strike two cracked target points in a clean one-two rhythm.', '0a0aa82e64b87b6bf38870d2da627784', 'ironclad/one_two_punch.png', 'cobaltCoral', 'character'],
  KarateRollingStone: ['One large turquoise boulder rolls along a red arc while one gloved fist gives it a golden impulse.', 'e69e1d52598a75cc351e2870cdace3dd', 'colorless/rolling_boulder.png', 'redCyan', 'partial'],
  KarateFinish: ['One decisive red-black chop splits a single cyan target silhouette along a bright gold diagonal.', 'cbb1dcac77249306f8eed2d697458c80', 'silent/finisher.png', 'tealAmber', 'character'],
  KarateWall: ['A guarded Ninja Slayer stands before four large elemental wedges: cyan wind, green forest, red fire, and dark mountain, with no symbols or writing.', '9ca79bbafca0c83d90eed899ab658588', 'ironclad/barricade.png', 'redCyan', 'character'],
  AlabamaDrop: ['Ninja Slayer completes one dramatic body drop against a simple turquoise training dummy, framed by one orange impact fan.', 'dce3d1f4a1c7c99269690e257aad5409', 'ironclad/body_slam.png', 'tealAmber', 'character'],
  CollapseFist: ['A vertical ancient-card composition: one enormous black-red fist collapses a turquoise stone column beneath one gold impact wedge.', '40182d846ee4e4529c3f3eea85c2d51b', 'ironclad/break.png', 'redCyan', 'character'],

  BurningStrike: ['One blackened fist wrapped in a single violet-black Naraku flame trail drives through a broad scarlet target plane.', '8ce9061cba125533d1e21fde532199d4', 'ironclad/molten_fist.png', 'narakuPurpleBlack', 'character'],
  NarakuRecovery: ['A crouched Ninja Slayer rises inside one black-purple Naraku silhouette while one broad violet life ring closes around him.', '78af0746fd0912372522d2a6dca128c4', 'ironclad/demon_form.png', 'narakuPurpleBlack', 'character'],
  RedBlackFlame: ['Ninja Slayer opens both arms as one red-black flame mass separates into three broad attack paths over a deep purple Naraku void.', '8d0a059ee215970c57b58f8f69c428ad', 'ironclad/demon_form.png', 'narakuPurpleBlack', 'character'],
  OneBodyOneSoul: ['A vertical ancient-card composition: one centered Ninja Slayer stands calmly while one translucent black-purple Naraku shadow mirrors him and one broad violet-red field joins them.', '25c6a211efd201f4fc285a050f1c26a7', 'ironclad/break.png', 'narakuPurpleBlack', 'character'],
  BurningCard: ['A single black-red flame burns on a cracked turquoise floor, with one amber inner core and no character.', 'b7c485cf31a47cb3266c534341a19fdb', 'status/burn.png', 'redCyan', 'object'],

  StrikeNinjaSlayer: ['A cropped Ninja Slayer delivers one simple side strike into a turquoise training block with one amber impact plane.', '39159d935cb7e10ecc7c1585cb024076', 'ironclad/strike_ironclad.png', 'redCyan', 'character'],
  DefendNinjaSlayer: ['Crossed black-red forearms brace behind one large cyan shield slab as a single orange strike glances away.', '9ca79bbafca0c83d90eed899ab658588', 'ironclad/defend_ironclad.png', 'indigoOrange', 'partial'],
  NinjaApathy: ['One gloved hand calmly retrieves a single shuriken from a purple ground plane while a discarded red motion ribbon recedes.', '56f26d1167f6eefa46ab55f7fa0ab014', 'silent/prepared.png', 'magentaLime', 'partial'],
  IHit: ['One straight punch is powered by one turquoise arc of spilled tea and lands on an amber target.', '40182d846ee4e4529c3f3eea85c2d51b', 'ironclad/uppercut.png', 'tealAmber', 'character'],
  DiscardDefense: ['Two dark paper-like shapes are cast aside and unfold into one broad cyan wall before a cropped red-black arm.', 'f99b2b80b5cefd7c854ecfa0c8452935', 'ironclad/defend_ironclad.png', 'redCyan', 'partial'],
  LuckyStrike: ['A close dark mask sees one bright cyan safe path between one orange blade wedge and one blue shield wedge.', '960ef31779ed34dddc567ade6f3773fa', 'colorless/thinking_ahead.png', 'indigoOrange', 'character'],
  NinjaWhip: ['One punch continues into a broad red scarf-whip arc that crosses a cyan target plane.', '70943036c0f3b24611220f78d7be3bdb', 'ironclad/twin_strike.png', 'redCyan', 'character'],
  OpeningGuard: ['One forearm block redirects an orange enemy arm and exposes one clean cyan opening beyond it.', '9ca79bbafca0c83d90eed899ab658588', 'silent/neutralize.png', 'indigoOrange', 'partial'],
  Evade: ['A cropped Ninja Slayer slips between exactly two broad orange attack streaks along one clean cyan escape path.', 'f9d63baecb3a4e2d2de264dabce97ea7', 'silent/blur.png', 'indigoOrange', 'character'],
  NinjaGreeting: ['Ninja Slayer gives one formal bow with hands together while red and cyan energy planes remain in balanced tension.', '7a3c1fe7021752c1a051965afc7ae969', 'ironclad/battle_trance.png', 'redCyan', 'character'],
  IBlock: ['One cyan wall stores a large gold glow behind it while a red-black forearm keeps the barrier planted.', '9ca79bbafca0c83d90eed899ab658588', 'ironclad/barricade.png', 'tealAmber', 'partial'],
  Riffle: ['Two gloved hands peel apart one turquoise guard bar, exposing a single orange weakness line.', 'bb8bc542ae584a3ff0602d13705c578d', 'silent/neutralize.png', 'tealAmber', 'partial'],
  MasochisticBliss: ['Three black curse spikes enter one red silhouette and emerge as one broad cyan-gold vigor flare.', 'b12fef5455f0a447e16f8103140ddafe', 'ironclad/dark_embrace.png', 'redCyan', 'partial'],
  ForgoStrength: ['One cracked red bracer is cast away from an open hand while a large cyan energy shape rises from the sacrifice.', '85244282a85b5fb771f152dc86c77075', 'ironclad/second_wind.png', 'redCyan', 'partial'],
  BloodTears: ['A close dark menpo sheds exactly two red tear streams while one broad cyan-gold energy halo opens behind it.', '960ef31779ed34dddc567ade6f3773fa', 'ironclad/dark_embrace.png', 'redCyan', 'character'],
  Evolution: ['A cropped torso grows three large turquoise armor plates as one orange status mote strikes the surface.', '9ca79bbafca0c83d90eed899ab658588', 'ironclad/stone_armor.png', 'tealAmber', 'partial'],
  Momentum: ['One forward-running Ninja Slayer follows a broad red wedge that changes into a cyan-gold acceleration wedge.', '713318f9e5fd047f7285fbd1844269cc', 'silent/blur.png', 'redCyan', 'character'],
  PursuitStrike: ['Ninja Slayer lunges after one simple violet shadow target along two long cyan and orange speed planes.', 'fe919d002c1e1cff101395c72d579ec5', 'silent/untouchable.png', 'indigoOrange', 'character'],
  LockOn: ['A close dark menpo fixes red eyes on one cyan target silhouette illuminated by two converging gold beams; no reticle or UI.', '960ef31779ed34dddc567ade6f3773fa', 'ironclad/evil_eye.png', 'redCyan', 'character'],
  TornadoFist: ['One punching arm turns inside two broad cyan tornado rings, ending in one orange impact shape; no repeated fists.', '1e800ea63b8af9a6a0ae44336bfe979a', 'ironclad/whirlwind.png', 'indigoOrange', 'partial'],
  NinjaSlayerFootwork: ['Only Ninja Slayer\'s lower legs are shown stepping between one red blade plane and one cyan blade plane.', 'd9288317124783f6c041cb570180db36', 'silent/footwork.png', 'redCyan', 'partial'],
  Recycle: ['One gloved hand catches a returning red scarf-shaped strike after it follows one complete cyan circular arc.', '2f0c54f73df4e65f5c20364b4d737e41', 'ironclad/sword_boomerang.png', 'redCyan', 'partial'],
  KillingIntent: ['A calm guarded Ninja Slayer reflects one orange attack back along a single cyan plane without changing stance.', '9ca79bbafca0c83d90eed899ab658588', 'ironclad/flame_barrier.png', 'indigoOrange', 'character'],
  TrueNameRead: ['One raised black-red hand receives a simple gold artifact disc above a broad cyan shadow; no writing or glyphs.', '9efeadac9cebceb549a607cef4d0e982', 'colorless/equilibrium.png', 'tealAmber', 'partial'],
  BusyLine: ['An old black IRC terminal, one red dead waveform, and one disconnected cyan cable form a simple status-card still life; the screen has no readable text.', '167dd11447efa51ddf9c4e608cb4c7a7', 'status/void.png', 'redCyan', 'object'],
};

function filesUnder(directory) {
  return readdirSync(directory, { withFileTypes: true }).flatMap((entry) => {
    const path = join(directory, entry.name);
    return entry.isDirectory() ? filesUnder(path) : [path];
  });
}

function upperSnake(name) {
  return name
    .replace(/([A-Z]+)([A-Z][a-z])/g, '$1_$2')
    .replace(/([a-z0-9])([A-Z])/g, '$1_$2')
    .toUpperCase();
}

function parseCatalog(classNames) {
  const source = readFileSync(join(repoRoot, 'Docs', 'card-catalog.md'), 'utf8');
  const rows = new Map();
  let theme = '';
  for (const line of source.split(/\r?\n/)) {
    const heading = /^## (.+)$/.exec(line);
    if (heading) theme = heading[1];
    const card = /^(.+?)\/([A-Za-z][A-Za-z0-9]+)\s+([^/]+)\/([^ ]+)/.exec(line);
    if (card && classNames.has(card[2])) {
      rows.set(card[2], { theme, catalogTitle: card[1], catalogRarity: card[3], catalogType: card[4] });
    }
  }
  return rows;
}

const cardFiles = filesUnder(join(repoRoot, 'Cards'))
  .filter((path) => path.endsWith('.cs') && !path.includes(join('Cards', 'Base')))
  .sort();
const cards = cardFiles.map((path) => {
  const source = readFileSync(path, 'utf8');
  const className = /public\s+sealed\s+class\s+(\w+)/.exec(source)?.[1];
  if (!className) throw new Error(`Missing card class in ${relative(repoRoot, path)}`);
  const normalized = source.replace(/\s+/g, ' ');
  const spec = /NinjaSlayerCardSpec CardSpec = new\(\s*nameof\((\w+)\),\s*(-?\d+),\s*CardType\.(\w+),\s*CardRarity\.(\w+),\s*([\w.]+),\s*(true|false)(?:,\s*"([^"]+)")?/.exec(normalized);
  if (!spec) throw new Error(`Cannot parse CardSpec in ${relative(repoRoot, path)}`);
  return {
    className,
    sourcePath: relative(repoRoot, path).replaceAll('\\', '/'),
    energyCost: Number(spec[2]),
    cardType: spec[3],
    rarity: spec[4],
    targetType: spec[5],
    assetAlias: spec[7] ?? null,
  };
});

const classNames = new Set(cards.map((card) => card.className));
const localization = JSON.parse(readFileSync(join(repoRoot, 'NinjaSlayer', 'localization', 'zhs', 'cards.json'), 'utf8'));
const catalog = parseCatalog(classNames);
const themeSlugs = { '手里剑': 'shuriken', '茶道': 'chado', '空手道': 'karate', '奈落': 'naraku', '其他': 'other' };

if (cards.length !== 93 || Object.keys(designs).length !== 93) {
  throw new Error(`Expected 93 cards and designs, found ${cards.length} cards and ${Object.keys(designs).length} designs`);
}
const missingDesigns = cards.filter((card) => !designs[card.className]).map((card) => card.className);
const unknownDesigns = Object.keys(designs).filter((name) => !classNames.has(name));
if (missingDesigns.length || unknownDesigns.length) {
  throw new Error(`Design mismatch. Missing: ${missingDesigns.join(', ')}. Unknown: ${unknownDesigns.join(', ')}`);
}

const entries = cards.map((card) => {
  const [scene, actionHash, styleRelativePath, paletteKey, characterMode] = designs[card.className];
  const catalogEntry = catalog.get(card.className);
  if (!catalogEntry) throw new Error(`Missing catalog entry for ${card.className}`);
  const localizationPrefix = `NINJA_SLAYER_CARD_${upperSnake(card.className)}`;
  const chineseName = localization[`${localizationPrefix}.title`];
  const description = localization[`${localizationPrefix}.description`] ?? '';
  if (!chineseName) throw new Error(`Missing title for ${card.className}`);
  const actionReference = join(actionRoot, `${actionHash}.png`);
  const styleReference = join(styleRoot, ...styleRelativePath.split('/'));
  if (!existsSync(actionReference)) throw new Error(`Missing action reference ${actionReference}`);
  if (!existsSync(styleReference)) throw new Error(`Missing style reference ${styleReference}`);
  const isAncient = card.rarity === 'Ancient';
  const generationGroup = card.rarity === 'Token' || card.rarity === 'Status'
    ? 'token-status'
    : themeSlugs[catalogEntry.theme];
  if (!generationGroup) throw new Error(`Unknown theme ${catalogEntry.theme} for ${card.className}`);
  const dimensions = isAncient ? { width: 606, height: 852 } : { width: 1000, height: 760 };
  const orientation = isAncient ? 'portrait 101:142 aspect ratio' : 'landscape 25:19 aspect ratio';
  const characterInstruction = characterMode === 'object'
    ? 'Do not add Ninja Slayer or any other person; the object or symbolic composition is the complete subject.'
    : characterMode === 'partial'
      ? 'Show only the body parts required by the scene. If the face appears, the menpo requirements below are mandatory.'
      : 'Show exactly one Ninja Slayer with anatomically correct connected limbs.';
  const prompt = `Use case: stylized-concept\nAsset type: collectible card portrait, ${orientation}\nPrimary request: Create an original illustration for ${card.className} (${chineseName}). ${scene}\nInput images: Image 1 is the character identity reference only. Image 2 is the action reference only; use its body mechanics or object interaction, but never copy its manga line art, panels, lettering, or exact composition. Image 3 is the strict controlling style, color-block, composition-density, focal scale, crop, and shape-count reference. Image 3 overrides Images 1 and 2 for every question of rendering detail and visual density; match its economical visual grammar without copying its depicted character or objects. Do not reproduce any isolated dark contour strokes visible in Image 3.\nCard meaning: ${description.replace(/\n/g, ' ').replace(/\[[^\]]+\]/g, '').replace(/\{[^}]+\}/g, 'value')}\nSubject rule: ${characterInstruction}\nStyle/medium: simplified hand-painted game-card cartoon in the visual language of Image 3; matte opaque fills, chunky asymmetrical silhouettes, and only a few broad hard-edged cel-shaded planes. Define every edge directly by adjacent contrasting hue or value shapes. Use no drawn outline strokes: no black ink contour, no colored contour, and no border tracing a silhouette or interior form. Black and charcoal may appear only as filled shapes for the menpo, costume, cast shadow, or negative space, never as a stroke around another shape. Give every body part, garment, prop, and effect a maximum of two flat tones: one base shape and at most one large shadow or highlight shape. Keep armor and cloth as broad uninterrupted masses with no internal panel rendering, surface brush texture, material grain, rows of rivets, or dense folds. Prefer a tighter crop over showing extra anatomy or costume detail. This must not look like polished vector poster art, a modern superhero splash page, detailed concept art, sticker art, or inked comic art.\nComposition density: one focal subject, one main action shape, and no more than three supporting elements unless the scene specifies a lower exact count. Aim for roughly 8 to 16 large contiguous color regions across the whole image; never subdivide a garment, limb, prop, or background merely to add visual interest. Preserve large quiet color areas and thumbnail readability. Never fill the background with repeated particles, projectiles, debris, cracks, folds, or effects.\nColor palette: ${palettes[paletteKey]}. At least two strongly separated dominant hue families must each occupy a meaningful large area; tiny accents do not satisfy this rule.\nCharacter consistency: when Ninja Slayer's face is visible, use the red-black hood and a menpo whose visible surface is overwhelmingly matte near-black or deep charcoal. At least four-fifths of the mask must read as black, not silver or reflective metal. Limit metallic treatment to one or two very narrow, muted dark-gray edge highlights; never use a bright metal faceplate. Do not force any lettering onto the mask. If a tiny low-contrast vertical engraving emerges naturally, it may loosely echo the traditional inscription in Image 1, but a plain matte-black mask is preferred over malformed or conspicuous writing. Mask lettering is not a generation or acceptance requirement. Preserve the long red-black scarf when the composition includes the upper body.\nOutput intent: opaque full-bleed RGB artwork with crop-safe margins for ${dimensions.width}x${dimensions.height}.\nAvoid: any outline strokes, black ink contours, colored border contours, uniform borders, sticker-like silhouette tracing, fine linework, hatching, crosshatching, brush noise, detailed fabric or armor texture, glossy chrome, bright silver faceplates, gradients, tiny highlights, dense folds, internal armor panels, excessive cracks or fragments, repeated small motifs, malformed anatomy, duplicate limbs, extra people, photorealism; prominent text, letters, numbers, pseudo-writing, speech bubbles, sound effects, manga panels, card frame, UI, logo, watermark, and signature.`;
  const framingLock = isAncient
    ? 'Portrait framing is mandatory: height must be noticeably greater than width, and every required subject and effect must remain inside the central 101:142 safe area.'
    : 'Landscape framing is mandatory: width must be noticeably greater than height, and every required subject, limb, prop, and effect must remain inside the central 25:19 horizontal safe area. Never compose this as a portrait image.';
  const finalPrompt = prompt.replace(
    /Character consistency:[^\n]+/,
    "Character consistency: whenever Ninja Slayer's costume is visible, use substantial scarlet or dark-red filled shapes for the hood, long scarf, and outer garment over a charcoal-black inner suit. Red must read as broad costume fill, never merely as a thin rim light, border, or outline around black clothing. When the face is visible, make the menpo one overwhelmingly matte near-black or deep-charcoal filled shape integrated into the red hood. At least four-fifths of the mask must read as black, not silver or reflective metal. Limit metallic treatment to at most one very small, muted dark-gray edge plane; never use a bright, blue-gray, silver, or reflective faceplate. Do not force any lettering onto the mask. If a tiny low-contrast vertical engraving emerges naturally, it may loosely echo the traditional inscription in Image 1, but a plain matte-black mask is preferred over malformed or conspicuous writing. Mask lettering is not a generation or acceptance requirement.",
  ).replace(
    '\nOutput intent:',
    `\nFraming lock: ${framingLock}\nOutput intent:`,
  );
  return {
    ...card,
    chineseName,
    description,
    theme: catalogEntry.theme,
    generationGroup,
    dimensions,
    characterMode,
    sceneBrief: scene,
    palette: palettes[paletteKey],
    qaChecklist: {
      economicalStyleMatch: 'Few large hard-edged color shapes, strong negative space, and no detailed concept-art rendering.',
      edgeTreatment: 'Every form is separated by adjacent hue/value shapes; no black or colored outline strokes, border tracing, or sticker-like silhouette treatment.',
      dominantHueSeparation: 'At least two substantially sized and clearly different hue families.',
      anatomyAndCrop: 'Connected anatomy, no duplicate limbs, and crop-safe focal action.',
      objectCount: 'No repeated-object clutter; any exact count in the scene brief is respected.',
      visibleMenpo: 'If visible, the menpo reads overwhelmingly matte black/deep charcoal with only narrow muted edge highlights.',
      menpoEngraving: 'Advisory only and not evaluated: prefer a plain matte-black menpo over forced, malformed, or conspicuous lettering.',
      forbiddenContent: 'No other text, pseudo-writing, panels, card frame, UI, logo, watermark, or signature.',
    },
    characterReference,
    actionReference,
    styleReference,
    outputPath: join(generatedRoot, generationGroup, `${card.className}.png`),
    finalPrompt,
  };
});

if (!existsSync(characterReference)) throw new Error(`Missing character reference ${characterReference}`);
mkdirSync(dirname(outputPath), { recursive: true });
for (const entry of entries) mkdirSync(dirname(entry.outputPath), { recursive: true });
writeFileSync(outputPath, `${JSON.stringify({ schemaVersion: 1, cardCount: entries.length, entries }, null, 2)}\n`, 'utf8');
console.log(`Wrote ${entries.length} card-art entries to ${outputPath}`);
