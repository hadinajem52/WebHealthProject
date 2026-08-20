import { copyFile, mkdir } from "node:fs/promises";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const root = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const source = resolve(root, "node_modules/chart.js/dist/chart.umd.js");
const destination = resolve(root, "src/WebHealth.Web/wwwroot/lib/chartjs/dist/chart.umd.js");

await mkdir(dirname(destination), { recursive: true });
await copyFile(source, destination);
