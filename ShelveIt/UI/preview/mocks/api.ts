import { useSyncExternalStore } from "react";

// Stand-in for cs2/api: bindings are plain keys in a store, triggers call the fake game side.

type Listener = () => void;

const values = new Map<string, unknown>();
const listeners = new Map<string, Set<Listener>>();
const handlers = new Map<string, (...args: any[]) => void>();

export function setValue(group: string, name: string, value: unknown) {
  const key = group + "." + name;

  values.set(key, value);
  listeners.get(key)?.forEach((listener) => listener());
}

export function getValue<T>(group: string, name: string): T {
  return values.get(group + "." + name) as T;
}

export function onTrigger(group: string, name: string, handler: (...args: any[]) => void) {
  handlers.set(group + "." + name, handler);
}

export function bindValue<T>(group: string, name: string, fallback?: T) {
  const key = group + "." + name;

  if (!values.has(key)) {
    values.set(key, fallback);
  }

  return {
    key,
    get value() {
      return values.get(key) as T;
    },
  };
}

export function useValue<T>(binding: { key: string }): T {
  return useSyncExternalStore(
    (listener) => {
      let set = listeners.get(binding.key);

      if (!set) {
        listeners.set(binding.key, (set = new Set()));
      }

      set.add(listener);

      return () => set!.delete(listener);
    },
    () => values.get(binding.key) as T
  );
}

export function trigger(group: string, name: string, ...args: unknown[]) {
  const handler = handlers.get(group + "." + name);

  if (handler) {
    handler(...args);
  } else {
    console.info("[preview] trigger without a handler:", group, name, args);
  }
}
