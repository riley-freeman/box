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

    return {
        turubai: {
            window_get_global: () =>
                store(window),

            window_get_document: (win) =>
                store(get(win).document),

            document_create_element: (doc, ty_ptr, ty_len) =>
                store(get(doc).createElement(str(ty_ptr, ty_len))),

            document_query_selector: (doc, query_ptr, query_len) =>
                store(get(doc).querySelector(str(query_ptr, query_len))),

            html_element_set_attribute: (elem, attr_ptr, attr_len, val_ptr, val_len) => {
                const url = new URL(window.location.href);
                const username = url.pathname.split("/")[1];
                let attr = str(attr_ptr, attr_len);
                let val = str(val_ptr, val_len);
                if (username[0] === '@') {
                    if (attr === 'src') {
                        val = '/' + username + val;
                    }
                }

                get(elem).setAttribute(attr, val)
            },

            html_element_set_style: (elem, prop_ptr, prop_len, val_ptr, val_len) =>
                get(elem).style.setProperty(str(prop_ptr, prop_len), str(val_ptr, val_len)),

            html_element_set_text_content: (elem, text_ptr, text_len) => {
                get(elem).textContent = str(text_ptr, text_len);
            },

            html_element_append_child: (parent, child) =>
                get(parent).appendChild(get(child)),
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
            }
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
