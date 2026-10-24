# 电量适配与协议说明

## 现有实现

### 标准 HID

`UsbInventory.Scan` 使用 SetupAPI 的 PRESENT / ALLCLASSES 枚举 Windows PnP 清单，并经 Configuration Manager 父节点关联到物理 `USB\VID_…&PID_…` 设备。复合接口（`MI_`）和 HID 子节点合并到物理 USB 父节点，USB 集线器被排除。清单获取不打开设备，也不查询 HID 报告，因此可与电量读取独立执行。设备路径只在本地使用，对外仅保留散列标识。

`Native.Enumerate` 另行获取可查询的 HID 接口，使用相同物理父节点映射回清单。访问失败的设备仍保留在连接清单里，名称和连接状态不会因为 HID 句柄无法打开而消失。

`StandardBatteryAdapter` 只查询报告描述符明确声明的 Generic Device Controls / Battery Strength（Usage Page `0x06`、Usage `0x20`）。先查询 Feature，后查询 Input。只接受 Logical Min=0、Max=100 且取值在 0–100 内的值。不会向未知设备发送猜测出的厂商指令，也不会把任意字节或电压直接换算成百分比。

输入报告通过 `HidD_GetInputReport` 获取，不读取键盘 / 鼠标按键事件流。部分驱动不支持主动获取 Input 报告，此时无法得到电量。未来可为确定的设备添加专门的电量通知通道。

### Logitech HID++ 2.0

适配器只匹配 Logitech VID=`046D`、Usage Page=`FF00`、Usage=`0002`、输入/输出报告长度均为 20 的长报告接口。

- `0x11` 长报告，最多 700 ms 等待一次匹配回复。
- 应答检查 report ID、设备槽位、feature index、function/software ID，并过滤错误报文与其他客户端消息。
- Root `0x0000`：协议版本 ping（含回显校验）及 feature index 查询。
- `0x1004` Unified Battery：function `0x10`，解析百分比、档位和状态。
- `0x1000` Battery Status：function `0x00`；0 按未知处理，不显示伪造的 0%。
- `0x0005` Device Name / Type：用于读取名称和设备类别，不根据共享接收器 PID 冒认型号。
- 接收器 PID `C500–C5FF` 尝试槽位 1–6；直接连接设备使用 `FF`。接收器名称不自动等同于设备名称。

不写 DPI、灯效、配置、配对或固件。发送的是查询请求。与厂商软件共用设备时，访问权限或协议交互可能影响成功率；未做真实罗技硬件兼容性验证。

电量硬件查询在独立的 `--probe` 子进程运行，25 秒超时终止；清单先发布，设备电量逐步保存。配置和扫描 JSON 通过临时文件替换，避免半写入文件。电量查询默认 60 秒，连接清单另行每 3 秒检查一次。

`DeviceNotifications` 通过 `RegisterDeviceNotification` 注册所有设备接口类型，接收隐藏窗口的到达 / 移除事件。界面 250 ms 合并通知。`DeviceMonitor` 对拓扑变化使用版本编号使旧响应失效，取消旧电量子进程，合并繁忙期间的新请求，并在清单更新后补查电量。即使迟到回复声称设备存在，也不能恢复最新清单里已经没有的设备。完整接收器移除会删除所有配对子设备。

## 新增品牌适配器

1. 在程序设置中导出真实设备诊断。确认 VID/PID、接口 Usage、报告长度和实际型号；必要时使用厂商公开文档或有合法授权的设备抓取查询流量。
2. 新建实现 `IBatteryAdapter` 的 C# 类，`CanHandle` 严格匹配确认过的设备和接口。未知设备不得仅凭“鼠标”或“2.4G”标签接收私有命令。
3. `Read` 返回 `DeviceState`。百分比仅在来源明确且数值有效时赋值；未知值保留 `null`。档位放在 `Level` 中。充电状态仅在协议支持时提供。
4. 将适配器加入 `Scanner.Scan` 的列表，在标准 HID 回退前执行。一个接收器连接多个外设时，为各设备生成稳定且不同的标识。
5. 编写报文测试，至少涵盖正常、低电量、充电、无效值、截断报文、其他设备回复、超时。先核对厂商软件，再在实机验证休眠、唤醒、拔插和多设备。
6. 需要真实缩略图时扩展 `device-catalog.json`，确保设备名称精确匹配；只分发有权使用的图片。

当前 `IBatteryAdapter` 为内部源码接口，需要重新构建，不在运行时加载未经验证的 DLL。

## 协议参考

- Microsoft：[HID API](https://learn.microsoft.com/en-us/windows-hardware/drivers/hid/)、[HidP_GetValueCaps](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/hidpi/nf-hidpi-hidp_getvaluecaps)、[HIDP_VALUE_CAPS](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/hidpi/ns-hidpi-_hidp_value_caps)。
- USB-IF：[HID Usage Tables](https://usb.org/document-library/hid-usage-tables-15)。
- Logitech：[公开 HID++ 文档](https://github.com/Logitech/cpg-docs)。
- Solaar：[HID++ 功能与报文行为参考](https://github.com/pwr-Solaar/Solaar/tree/master/lib/logitech_receiver)。参考其协议字段含义，本项目未分发或引入 Solaar 的 Python 实现。
- mingw-w64：[Windows HID 结构声明参考](https://github.com/mingw-w64/mingw-w64/blob/master/mingw-w64-headers/include/hidpi.h)。

本项目使用独立 C# 实现，不链接上述项目。文档、协议资料和第三方项目的许可证归各自项目所有。
