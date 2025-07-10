# BNMGameStructureGenerator


https://github.com/Pubert-CS/BNM-Il2CppSDKGenerator might be better on what your doing

---

## 📖 How to Use

1. **Compile** this tool (or use the release version, if available).
2. Dump your game’s `DummyDll` using [Il2CppDumper](https://github.com/Perfare/Il2CppDumper).
3. Place the entire `DummyDll` folder inside a new folder named `Files` in the same directory as this tool’s executable.
4. Run the tool and wait for your SDK to be generated automatically.
5. Copy the generated SDK into your project.
6. Copy the pre-written Il2Cpp headers into your project, and include them in your `Android.mk` or `CMakeLists.txt`.

✅ Done!

---

### 🔧 Extra Options:

* Use `-s` or `--single-file` to output the entire SDK in **one single file**.
