const node = (tag, text, className) => {
  const item = document.createElement(tag);
  if (text !== undefined) item.textContent = text;
  if (className) item.className = className;
  return item;
};
const cache = new Map();
export async function loadCatalog(version) {
  if (!version) return null;
  if (!cache.has(version))
    cache.set(
      version,
      fetch(
        `./content/versions/${encodeURIComponent(version)}/catalog.json`,
      ).then(async (response) => (response.ok ? response.json() : null)),
    );
  return cache.get(version);
}
const plain = (text) =>
  String(text ?? "")
    .replace(
      /\[\/?(?:b|i|u|s|gold|red|green|blue|purple|orange|color(?:=[^\]]*)?|font(?:=[^\]]*)?)\]/g,
      "",
    )
    .replace(
      /\[(?:energy|star|[a-z_]+_icon)\]/g,
      (match) => ({ "[energy]": "◆", "[star]": "★" })[match] ?? match,
    );
export async function cardPreview(id, version, container, onCard) {
  container.replaceChildren(node("p", "加载卡牌…"));
  const catalog = await loadCatalog(version);
  const model = catalog?.languages.zhs.find((model) => model.id === id);
  if (!model) {
    container.replaceChildren(node("p", "暂无此版本的卡牌资料。"));
    return;
  }
  const render = (upgraded) => {
    container.replaceChildren();
    const card = node("article", undefined, "game-card"),
      variant = model.variants?.[Number(upgraded)];
    if (model.image) {
      const image = node("img");
      image.src = `./content/${model.image}`;
      image.alt = variant?.name ?? model.name;
      image.loading = "lazy";
      card.append(image);
    }
    card.append(
      node("h3", variant?.name ?? model.name),
      node(
        "p",
        plain(variant?.description ?? model.description),
        "card-description",
      ),
    );
    if (variant)
      card.prepend(
        node(
          "span",
          variant.costsX ? "X" : variant.cost < 0 ? "—" : variant.cost,
          "energy-cost",
        ),
      );
    const aside = node("div", undefined, "card-tips");
    if (model.variants?.length > 1) {
      const toggle = node(
        "button",
        upgraded ? "查看基础牌" : "查看升级牌",
        "button",
      );
      toggle.onclick = () => render(!upgraded);
      aside.append(toggle);
    }
    for (const tip of variant?.tips ?? model.tips ?? []) {
      if (tip.card) {
        const next = node(
          "button",
          (catalog.languages.zhs.find((card) => card.id === tip.card)
            ?.variants?.[0].name ?? tip.card) + (tip.upgraded ? " +" : ""),
          "button",
        );
        next.onclick = () => onCard(tip.card, version);
        aside.append(next);
      } else if (tip.description) {
        const section = node("section");
        section.append(
          node("h4", tip.title),
          node("p", plain(tip.description)),
        );
        aside.append(section);
      }
    }
    const block = node("div", undefined, "preview-layout");
    block.append(card, aside);
    container.append(block);
  };
  render(false);
}
