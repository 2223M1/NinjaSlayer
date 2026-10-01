import yauzl from 'yauzl';

// Browse in memory; no member name is ever passed to a filesystem API.
export function inspectFeedbackZip(bytes, requestedIndex = null) {
  if (bytes.length > 64 * 1024 * 1024) throw new Error('附件包超过 64 MiB，请下载原始文件查看。');
  return new Promise((resolve, reject) => {
    yauzl.fromBuffer(bytes, { lazyEntries: true, validateEntrySizes: true }, (error, zip) => {
      if (error) { reject(new Error('附件不是有效的 ZIP 文件。')); return; }
      const entries = []; let total = 0, selected;
      const fail = error => { zip.close(); reject(error); };
      zip.on('error', fail);
      zip.on('entry', entry => {
        const index = entries.length;
        total += entry.uncompressedSize;
        if (index >= 2048 || total > 256 * 1024 * 1024 || entry.uncompressedSize > 64 * 1024 * 1024) {
          fail(new Error('附件展开体积超出预览限制，请下载原始 ZIP。')); return;
        }
        entries.push({ index, name: entry.fileName, size: entry.uncompressedSize, directory: entry.fileName.endsWith('/') });
        if (index !== requestedIndex) { zip.readEntry(); return; }
        if (entry.generalPurposeBitFlag & 1) { fail(new Error('无法预览加密附件。')); return; }
        zip.openReadStream(entry, (error, stream) => {
          if (error) { fail(error); return; }
          const chunks = []; let count = 0;
          stream.on('error', fail);
          stream.on('data', chunk => { count += chunk.length; if (count > 64 * 1024 * 1024) stream.destroy(new Error('附件过大。')); else chunks.push(chunk); });
          stream.on('end', () => { selected = { ...entries[index], bytes: Buffer.concat(chunks) }; zip.readEntry(); });
        });
      });
      zip.on('end', () => {
        zip.close();
        if (requestedIndex !== null && !selected) reject(new Error('找不到此附件。'));
        else resolve(requestedIndex === null ? entries : selected);
      });
      zip.readEntry();
    });
  });
}
