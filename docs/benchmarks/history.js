/* No external chart service; data is copied from benchmark-data during the existing Pages build. */
(async () => {
  const get = id => document.getElementById(id);
  const status = get('status');
  const data = new URL('data/', document.currentScript.src);
  const cell = (row, value) => {
    const td = document.createElement('td');
    td.textContent = value;
    row.append(td);
    return td;
  };
  const link = (parent, label, url) => {
    const a = document.createElement('a');
    a.textContent = label;
    a.href = url;
    parent.append(a, ' ');
  };
  const num = value => value == null ? 'unsupported / missing' : value.toLocaleString(undefined, {maximumFractionDigits: 2});
  try {
    const response = await fetch(new URL('index.json', data));
    if (!response.ok) throw new Error(`Index HTTP ${response.status}`);
    const index = await response.json();
    if (index.schema !== 1) throw new Error(`Unsupported index schema ${index.schema}`);
    const records = await Promise.all(index.records.map(async entry => {
      const url = new URL(entry.path, data);
      const response = await fetch(url);
      if (!response.ok) throw new Error(`Record HTTP ${response.status}`);
      const record = await response.json();
      if (record.schema !== 1) throw new Error(`Unsupported record schema ${record.schema}`);
      return {record, url};
    }));
    if (!records.length) {
      status.textContent = 'No measured history has been published. A TaskFlow 1.0.0 baseline is not yet available.';
      return;
    }
    const groups = [...new Set(records.map(x => x.record.series))];
    for (const series of groups) {
      const first = records.find(x => x.record.series === series).record;
      const cpu = first.provenance.environment.cpu['/proc/cpuinfo'].match(/model name\s*:\s*(.*)/)?.[1] || 'CPU recorded in provenance';
      get('series').add(new Option(`${series.slice(0, 12)} · ${cpu} · SDK ${first.provenance.settings.sdk}`, series));
    }
    const baseline = records.find(x => x.record.provenance.label === 'TaskFlow 1.0.0');
    if (baseline) get('series').value = baseline.record.series;
    const svg = (name, attrs, text) => {
      const element = document.createElementNS('http://www.w3.org/2000/svg', name);
      for (const [key, value] of Object.entries(attrs)) element.setAttribute(key, value);
      if (text != null) element.textContent = text;
      get('chart').append(element);
      return element;
    };
    function render() {
      const selected = records.filter(x => x.record.series === get('series').value);
      const identity = get('case').value;
      const metric = get('metric').value;
      get('rows').replaceChildren();
      get('chart').replaceChildren();
      const max = Math.max(1, ...selected.map(x => x.record.cases[identity]?.[metric] || 0));
      svg('line', {x1: 75, y1: 240, x2: 820, y2: 240, stroke: '#888'});
      svg('text', {x: 5, y: 25, 'font-size': 12}, `${get('metric').selectedOptions[0].text}; max ${num(max)}`);
      let previous = null;
      selected.forEach(({record, url}, i) => {
        const p = record.provenance;
        const value = record.cases[identity];
        const row = document.createElement('tr');
        cell(row, p.label);
        link(row.lastChild, p.measured_sha.slice(0, 8), `https://github.com/dombrovsky/TaskFlow/commit/${p.measured_sha}`);
        cell(row, p.timestamp.slice(0, 19));
        cell(row, num(value?.mean_ns));
        cell(row, value ? `${num(value.stddev_ns)} / ${value.n}` : 'missing');
        cell(row, num(value?.ops_s));
        cell(row, num(value?.allocated_bytes));
        const evidence = cell(row, '');
        if (p.run_url) link(evidence, 'Run', p.run_url);
        link(evidence, 'Record', url);
        for (const [index, path] of record.raw_reports.entries()) link(evidence, `Raw ${index + 1}`, new URL(`raw/${path}`, url));
        get('rows').append(row);
        const point = value?.[metric];
        if (point == null) { previous = null; return; }
        const x = 100 + i * 680 / Math.max(1, selected.length - 1);
        const y = 240 - point / max * 180;
        if (previous) svg('line', {x1: previous.x, y1: previous.y, x2: x, y2: y, stroke: '#2865ad'});
        const dot = svg('circle', {cx: x, cy: y, r: 5, fill: '#2865ad'});
        const title = document.createElementNS('http://www.w3.org/2000/svg', 'title');
        title.textContent = `${p.label}: ${num(point)}; StdDev ${num(value.stddev_ns)} ns`;
        dot.append(title);
        if (metric === 'mean_ns') {
          const error = value.stddev_ns / max * 180;
          svg('line', {x1: x, x2: x, y1: y - error, y2: y + error, stroke: '#444'});
        }
        svg('text', {x, y: 265, 'text-anchor': 'middle', 'font-size': 11}, p.label);
        svg('text', {x, y: 285, 'text-anchor': 'middle', 'font-size': 10}, p.measured_sha.slice(0, 8));
        previous = {x, y};
      });
      get('provenance').textContent = JSON.stringify(selected.map(x => x.record.provenance), null, 2);
      status.textContent = `${selected.length} measured runs in this compatible series. ${baseline ? 'TaskFlow 1.0.0 baseline is available.' : 'TaskFlow 1.0.0 baseline has not been published.'}`;
    }
    function cases() {
      const selected = records.filter(x => x.record.series === get('series').value);
      get('case').replaceChildren();
      for (const key of [...new Set(selected.flatMap(x => Object.keys(x.record.cases)))].sort()) get('case').add(new Option(key, key));
      render();
    }
    get('series').onchange = cases;
    get('case').onchange = render;
    get('metric').onchange = render;
    cases();
  } catch (error) {
    status.textContent = `History unavailable: ${error.message}. No performance claim can be made.`;
  }
})();