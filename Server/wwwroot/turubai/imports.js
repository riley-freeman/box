/* @ts-self-types="./hello_world.d.ts" */

export function render_profile() {
    wasm.render_profile();
}

function getTurubaiImports(wasmMemory) {
    // Index 0 is reserved as the null/invalid handle.
    const objects = [null];

    function store(obj) {
        if (obj == null) return 0;
        objects.push(obj);
        return objects.length - 1;
    }

    function get(idx) {
        return objects[idx];
    }

    function str(ptr, len) {
        return new TextDecoder().decode(new Uint8Array(wasmMemory.buffer, ptr, len));
    }

    // Color variant -> CSS color string.
    // Variant 0 = Custom (RGBA packed as (r<<24)|(g<<16)|(b<<8)|a).
    function cssColor(variant, packed) {
        const map = {
            1: "inherit",
            128: "red", 129: "orange", 130: "yellow", 131: "green",
            132: "blue", 133: "indigo", 134: "purple", 135: "pink",
        };
        if (variant in map) return map[variant];
        const r = (packed >>> 24) & 0xFF;
        const g = (packed >>> 16) & 0xFF;
        const b = (packed >>> 8) & 0xFF;
        const a = (packed & 0xFF) / 255;
        return `rgba(${r},${g},${b},${a})`;
    }

    // Unit variant (0=Auto, 1=Pixels, 2=Percent, 3=Em) -> CSS string.
    function cssUnit(variant, value) {
        switch (variant) {
            case 0: return "auto";
            case 1: return `${value}px`;
            case 2: return `${value * 100}%`;
            case 3: return `${value}em`;
            default: return "auto";
        }
    }

    return {
        turubai: {
            // ── Context ────────────────────────────────────────────────────────

            glue_context_get_global: () =>
                store(window.document),

            glue_context_get_root: (context) => {
                const target = get(context).querySelector(".turubai-render-target");
                if (!target) return 0;
                const centerer = document.createElement("div");
                centerer.style.display = "flex";
                centerer.style.justifyContent = "center";
                centerer.style.alignItems = "center";
                centerer.style.width = "100%";
                centerer.style.height = "100%";
                centerer.style.boxSizing = "border-box";
                target.appendChild(centerer);
                return store(centerer);
            },

            // ── Text ───────────────────────────────────────────────────────────

            glue_context_create_text: (context) =>
                store(get(context).createElement("p")),

            glue_text_set_text: (text, ptr, len) => {
                get(text).textContent = str(ptr, len);
            },

            glue_text_set_color: (text, variant, packed) => {
                get(text).style.color = cssColor(variant, packed);
            },

            glue_text_set_font: (text, family_ptr, family_len, size, weight, italic) => {
                const elem = get(text);
                elem.style.fontFamily = str(family_ptr, family_len);
                elem.style.fontSize = `${size}pt`;
                elem.style.fontWeight = weight;
                elem.style.fontStyle = italic ? "italic" : "normal";
            },

            glue_text_set_decoration: (text, ul_style, ul_cv, ul_c, st_style, st_cv, st_c) => {
                const elem = get(text);
                const lineStyleMap = { 1: "solid", 2: "solid", 3: "double", 4: "dotted", 5: "dashed" };
                const lines = [];
                if (ul_style !== 0) lines.push("underline");
                if (st_style !== 0) lines.push("line-through");
                if (lines.length === 0) {
                    elem.style.textDecorationLine = "none";
                    return;
                }
                elem.style.textDecorationLine = lines.join(" ");
                const activeStyle = ul_style !== 0 ? ul_style : st_style;
                elem.style.textDecorationStyle = lineStyleMap[activeStyle] ?? "solid";
                const [cv, c] = ul_style !== 0 ? [ul_cv, ul_c] : [st_cv, st_c];
                elem.style.textDecorationColor = cssColor(cv, c);
            },

            // ── Picture ────────────────────────────────────────────────────────

            glue_context_create_picture: (context) => {
                const img = get(context).createElement("img");
                img.style.width = "100%";
                img.style.height = "100%";
                return store(img);
            },

            glue_picture_set_src: (picture, ptr, len) => {
                const url = new URL(window.location.href);
                const uname = url.pathname.split("/")[1];
                let src = str(ptr, len);
                if (uname[0] === '@')
                    src = '/' + uname + src;

                get(picture).src = src;
            },

            glue_picture_set_fitting: (picture, fit) => {
                // 0=Stretch, 1=Cover, 2=Contain
                get(picture).style.objectFit = ["fill", "cover", "contain"][fit] ?? "fill";
            },

            // ── HStack ─────────────────────────────────────────────────────────

            glue_context_create_hstack: (context) => {
                const div = get(context).createElement("div");
                div.style.display = "flex";
                div.style.flexDirection = "row";
                return store(div);
            },

            glue_hstack_set_spacing: (hstack, variant, value) => {
                get(hstack).style.gap = cssUnit(variant, value);
            },

            glue_hstack_set_alignment: (hstack, alignment) => {
                // VerticalAlignment: 0=Top, 1=Center, 2=Bottom
                get(hstack).style.alignItems = ["flex-start", "center", "flex-end"][alignment] ?? "flex-start";
            },

            glue_hstack_set_fills_width: (hstack, fills) => {
                if (fills) get(hstack).style.width = "100%";
            },

            // ── VStack ─────────────────────────────────────────────────────────

            glue_context_create_vstack: (context) => {
                const div = get(context).createElement("div");
                div.style.display = "flex";
                div.style.flexDirection = "column";
                return store(div);
            },

            glue_vstack_set_spacing: (vstack, variant, value) => {
                get(vstack).style.gap = cssUnit(variant, value);
            },

            glue_vstack_set_alignment: (vstack, alignment) => {
                // HorizontalAlignment: 0=Leading, 1=Center, 2=Trailing
                get(vstack).style.alignItems = ["flex-start", "center", "flex-end"][alignment] ?? "flex-start";
            },

            glue_vstack_set_fills_height: (vstack, fills) => {
                if (fills) get(vstack).style.height = "100%";
            },

            // ── Spacer ─────────────────────────────────────────────────────────

            glue_context_create_spacer: (context) => {
                const div = get(context).createElement("div");
                div.style.flex = "1";
                return store(div);
            },

            // ── Background ─────────────────────────────────────────────────────

            glue_context_create_background: (context) =>
                store(get(context).createElement("div")),

            glue_background_set_color: (bg, variant, packed) => {
                get(bg).style.backgroundColor = cssColor(variant, packed);
            },

            // ── Padding ────────────────────────────────────────────────────────

            glue_context_create_padding: (context) => {
                const div = get(context).createElement("div");
                div.style.boxSizing = "border-box";
                return store(div);
            },

            glue_padding_set_insets: (padding, top, left, bottom, right) => {
                const elem = get(padding);
                elem.style.paddingTop = `${top}px`;
                elem.style.paddingLeft = `${left}px`;
                elem.style.paddingBottom = `${bottom}px`;
                elem.style.paddingRight = `${right}px`;
            },

            // ── Frame ──────────────────────────────────────────────────────────

            glue_context_create_frame: (context) =>
                store(get(context).createElement("div")),

            glue_frame_set_max_width: (frame, v, val) => { get(frame).style.maxWidth = cssUnit(v, val); },
            glue_frame_set_max_height: (frame, v, val) => { get(frame).style.maxHeight = cssUnit(v, val); },
            glue_frame_set_min_width: (frame, v, val) => { get(frame).style.minWidth = cssUnit(v, val); },
            glue_frame_set_min_height: (frame, v, val) => { get(frame).style.minHeight = cssUnit(v, val); },
            glue_frame_set_width: (frame, v, val) => { get(frame).style.width = cssUnit(v, val); },
            glue_frame_set_height: (frame, v, val) => { get(frame).style.height = cssUnit(v, val); },

            // ── Group (Component / Window) ─────────────────────────────────────

            glue_context_create_group: (context) =>
                store(get(context).createElement("div")),

            // ── Node tree ──────────────────────────────────────────────────────

            glue_node_append_child: (parent, child) => {
                get(parent).appendChild(get(child));
            },

            glue_node_remove_child: (parent, child) => {
                get(parent).removeChild(get(child));
            },
        },
    };
}

function getMercyImports(wasmMemory) {
    function str(ptr, len) {
        return new TextDecoder().decode(new Uint8Array(wasmMemory.buffer, ptr, len));
    }

    let wasmExports = null;

    const imports = {
        mercy: {
            // Called by the Rust waker whenever a task becomes ready to poll.
            // Schedules a microtask so the executor runs after the current
            // JS turn finishes.
            schedule_poll: () => {
                queueMicrotask(() => wasmExports.mercy_poll());
            },

            console_log: (ptr, len) => console.log(str(ptr, len)),
            console_warn: (ptr, len) => console.warn(str(ptr, len)),
            console_error: (ptr, len) => console.error(str(ptr, len)),
        },
    };

    // Call immediately after WebAssembly.instantiate to wire up mercy_poll.
    imports.linkExports = function (exports) {
        wasmExports = exports;
    };

    return imports;
}

function getBoxBlocksImports(wasmMemory, wasmExports) {
    // Index 0 is reserved as the null/invalid handle.
    const objects = [null];

    function store(obj) {
        if (obj == null) return 0;
        objects.push(obj);
        return objects.length - 1;
    }

    function get(idx) {
        return objects[idx];
    }

    function str(ptr, len) {
        return new TextDecoder().decode(new Uint8Array(wasmMemory.buffer, ptr, len));
    }

    function writeStr(s) {
        const encoded = new TextEncoder().encode(s + '\0');
        const ptr = wasmExports.alloc(encoded.length);
        new Uint8Array(wasmMemory.buffer).set(encoded, ptr);
        return ptr;
    }

    return {
        box_blocks: {
            external_get_our_username: () => {
                const username = new URL(window.location.href).pathname.split("/")[1];
                return writeStr(username);
            },
            external_format_time: (time) => {
                const date = new Date(time * 1000);
                return writeStr(date.toLocaleString());
            },
            external_start_fetch_display_name: (usernamePtr, len, id) => {
                const username = str(usernamePtr, len);
                fetch(`/api/user/display-name?username=${encodeURIComponent(username)}`)
                    .then(res => res.json())
                    .then(data => {
                        const encoded = new TextEncoder().encode(data.displayName);
                        const ptr = wasmExports.alloc(encoded.length + 1);
                        const mem = new Uint8Array(wasmMemory.buffer);
                        mem.set(encoded, ptr);
                        mem[ptr + encoded.length] = 0;
                        wasmExports.on_display_name_fetched(id, ptr, encoded.length);
                    });
            },
            external_start_fetch_threads: (usernamePtr, usernameLen, offset, size, id) => {
                const username = str(usernamePtr, usernameLen).substring(1);
                fetch(`/api/user/threads?username=${encodeURIComponent(username)}&offset=${encodeURIComponent(offset)}&size=${encodeURIComponent(size)}`)
                    .then(res => res.json())
                    .then(data => {
                        console.log(data);
                        data.forEach(thread => {
                            function encodeString(str) {
                                const encoded = new TextEncoder().encode(str);
                                const ptr = wasmExports.alloc(encoded.length + 1);
                                const mem = new Uint8Array(wasmMemory.buffer);
                                mem.set(encoded, ptr);
                                mem[ptr + encoded.length] = 0;
                                return [encoded, ptr, encoded.length];
                            }

                            const [, pUsername, lUsername] = encodeString(thread.Username);
                            const [, pTitle, lTitle] = encodeString(thread.Title);
                            const [, pMessage, lMessage] = encodeString(thread.Message);

                            wasmExports.thread_fetched(id, pUsername, lUsername, pTitle, lTitle, pMessage, lMessage, thread.Time);
                        });
                        wasmExports.on_threads_fetched(id);
                    })
            },
        }
    }
}



let wasm;
let _mercyGlue = null;

function __wbg_get_imports() {
    const lazyMem = { get buffer() { return wasm.memory.buffer; } };
    const lazyExports = new Proxy({}, { get(_, k) { return wasm[k]; } });
    _mercyGlue = getMercyImports(lazyMem);
    return {
        turubai: getTurubaiImports(lazyMem).turubai,
        mercy: _mercyGlue.mercy,
        box_blocks: getBoxBlocksImports(lazyMem, lazyExports).box_blocks,
        // Stubs required until the WASM is rebuilt without wasm-bindgen.
        __wbindgen_placeholder__: {
            __wbindgen_describe: () => { },
            __wbindgen_describe_closure: () => { },
        },
        __wbindgen_externref_xform__: {
            __wbindgen_externref_table_set_null: (idx) => { wasm.__wbindgen_externrefs?.set(idx, null); },
            __wbindgen_externref_table_grow: (delta) => wasm.__wbindgen_externrefs?.grow(delta) ?? 0,
        },

    };
}

function __wbg_finalize_init(instance) {
    wasm = instance.exports;
    if (_mercyGlue) _mercyGlue.linkExports(wasm);
    return wasm;
}

async function __wbg_load(module, imports) {
    if (typeof Response === 'function' && module instanceof Response) {
        if (typeof WebAssembly.instantiateStreaming === 'function') {
            try {
                return await WebAssembly.instantiateStreaming(module, imports);
            } catch (e) {
                if (module.ok && module.headers.get('Content-Type') !== 'application/wasm') {
                    console.warn('instantiateStreaming failed (wrong MIME type), falling back:', e);
                } else {
                    throw e;
                }
            }
        }
        const bytes = await module.arrayBuffer();
        return await WebAssembly.instantiate(bytes, imports);
    } else {
        const instance = await WebAssembly.instantiate(module, imports);
        return instance instanceof WebAssembly.Instance
            ? { instance, module }
            : instance;
    }
}

function initSync(module) {
    if (wasm !== undefined) return wasm;
    if (Object.getPrototypeOf(module) === Object.prototype) ({ module } = module);
    const imports = __wbg_get_imports();
    if (!(module instanceof WebAssembly.Module)) module = new WebAssembly.Module(module);
    const instance = new WebAssembly.Instance(module, imports);
    return __wbg_finalize_init(instance);
}

async function __wbg_init(module_or_path) {
    if (wasm !== undefined) return wasm;
    if (Object.getPrototypeOf(module_or_path ?? {}) === Object.prototype) {
        ({ module_or_path } = module_or_path);
    }
    if (module_or_path === undefined) {
        module_or_path = new URL('hello_world.wasm', import.meta.url);
    }
    const imports = __wbg_get_imports();
    if (typeof module_or_path === 'string' ||
        (typeof Request === 'function' && module_or_path instanceof Request) ||
        (typeof URL === 'function' && module_or_path instanceof URL)) {
        module_or_path = fetch(module_or_path);
    }
    const { instance } = await __wbg_load(await module_or_path, imports);
    return __wbg_finalize_init(instance);
}

export { initSync, __wbg_init as default };
