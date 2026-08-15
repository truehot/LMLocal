'use strict';

/**
 * Map of file extensions to markdown language identifiers for code fences.
 */
const EXTENSION_LANG_MAP = {
    // .NET / C#
    cs: 'csharp',
    csx: 'csharp',          // C# scripts
    asax: 'csharp',         // global.asax (обычно C#)
    asmx: 'csharp',         // web services
    // Visual Basic
    vb: 'vbnet',
    // F#
    fs: 'fsharp',
    fsi: 'fsharp',
    fsx: 'fsharp',
    fsscript: 'fsharp',

    // Web & frontend
    html: 'html',
    htm: 'html',
    css: 'css',
    scss: 'scss',
    less: 'less',
    sass: 'sass',
    styl: 'stylus',
    js: 'javascript',
    jsx: 'jsx',             // React JSX
    ts: 'typescript',
    tsx: 'tsx',             // React TSX
    vue: 'vue',
    svelte: 'svelte',
    pug: 'pug',
    ejs: 'ejs',
    handlebars: 'handlebars',
    hbs: 'handlebars',
    twig: 'twig',
    jinja: 'jinja',
    // Razor (ASP.NET Core)
    razor: 'razor',
    cshtml: 'razor',
    vbhtml: 'razor',
    // Classic ASP.NET
    aspx: 'aspnet',
    ascx: 'aspnet',
    master: 'aspnet',
    ashx: 'aspnet',

    // Mobile / cross-platform
    swift: 'swift',
    kt: 'kotlin',
    kts: 'kotlin',          // Kotlin script
    dart: 'dart',

    // Systems & compiled languages
    cpp: 'cpp',
    cxx: 'cpp',
    cc: 'cpp',
    hpp: 'cpp',
    hh: 'cpp',
    hxx: 'cpp',
    c: 'c',
    h: 'c',                 // C header (can be C++, but we keep C)
    m: 'objectivec',
    mm: 'objectivec',
    rs: 'rust',
    go: 'go',
    zig: 'zig',

    // Scripting & interpreted
    py: 'python',
    pyw: 'python',
    rb: 'ruby',
    php: 'php',
    pl: 'perl',
    pm: 'perl',
    lua: 'lua',
    r: 'r',
    scala: 'scala',
    groovy: 'groovy',
    clj: 'clojure',
    cljs: 'clojure',
    ex: 'elixir',
    exs: 'elixir',
    erl: 'erlang',
    hrl: 'erlang',
    hs: 'haskell',
    lhs: 'haskell',

    // Markup, config, data
    md: 'markdown',
    mdx: 'mdx',
    json: 'json',
    jsonl: 'jsonl',
    yaml: 'yaml',
    yml: 'yaml',
    xml: 'xml',
    xsd: 'xml',
    xsl: 'xml',
    xslt: 'xml',
    svg: 'svg',
    toml: 'toml',
    ini: 'ini',
    cfg: 'ini',
    config: 'ini',
    conf: 'conf',
    env: 'env',
    'editorconfig': 'editorconfig',

    // Build & project files
    sln: 'xml',             // solution file
    csproj: 'xml',
    vbproj: 'xml',
    fsproj: 'xml',
    vcxproj: 'xml',
    resx: 'xml',
    props: 'xml',
    targets: 'xml',
    cmake: 'cmake',
    makefile: 'makefile',

    // Database
    sql: 'sql',
    psql: 'sql',

    // Shell / console
    sh: 'bash',
    bash: 'bash',
    zsh: 'bash',
    bat: 'batch',
    cmd: 'batch',
    ps1: 'powershell',
    psm1: 'powershell',
    psd1: 'powershell',

    // Other
    http: 'http',
    rest: 'http',
    graphql: 'graphql',
    gql: 'graphql',
    proto: 'protobuf',
    tf: 'terraform',
    tfvars: 'terraform',

    // Fallback / generic
    log: 'text',
    txt: 'text',
    text: 'text',
    '': 'text',         // files without extension
};


/**
 * Wraps file content in a markdown code fence with the appropriate language tag and adds a comment line with the filename using the correct comment syntax.
 */
export function wrapAsCodeFence(filename, content) {
    const ext = (filename.split('.').pop() || '').toLowerCase();
    const lang = EXTENSION_LANG_MAP[ext] || '';

    let commentChar;
    if (['csharp', 'javascript', 'typescript', 'java', 'cpp', 'c', 'go', 'rs', 'php', 'vue', 'svelte',
        'fsharp', 'swift', 'kotlin', 'dart', 'scala', 'groovy', 'rust', 'csx', 'jsx', 'tsx'
    ].includes(lang)) {
        commentChar = '//';
    } else if (['css', 'scss', 'less', 'sass', 'stylus'].includes(lang)) {
        commentChar = '/*';
    } else if (['html', 'xml', 'svg', 'xhtml'].includes(lang)) {
        commentChar = '<!--';
    } else if (['sql', 'plsql', 'psql'].includes(lang)) {
        commentChar = '--';
    } else if (['vbnet', 'vb'].includes(lang)) {
        commentChar = "'";
    } else if (['clojure', 'cljs'].includes(lang)) {
        commentChar = ';';
    } else if (['erlang', 'elixir'].includes(lang)) {
        commentChar = '%';
    } else if (['makefile', 'cmake'].includes(lang)) {
        commentChar = '#';
    } else {
        commentChar = '#';
    }

    let comment;
    if (['css', 'scss', 'less', 'sass', 'stylus'].includes(lang)) {
        comment = `${commentChar} ${filename} */`;
    } else if (['html', 'xml', 'svg', 'xhtml'].includes(lang)) {
        comment = `${commentChar} ${filename} -->`;
    } else {
        comment = `${commentChar} ${filename}`;
    }

    return `\n\`\`\`\`${lang}\n${comment}\n${content}\n\`\`\`\`\n`;
}