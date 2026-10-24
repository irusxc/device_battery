# 电量适配与协议说明

## 现有实现

### 标准 HID

`Native.Enumerate` 使用 SetupAPI 获取 HID 接口，读取厂商、产品名称和 HID capabilities。`DEVPKEY_Device_ContainerId` 与 VID/PID 用于合并接口；序列号不主动查询。只处理 `hid#vid_` 接口路径，USB HID 以外的设备需要额外发现器。

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

所有硬件查询在单独的 `--probe` 子进程运行。父进程超时会终止该进程，保留已经完成的扫描结果；配置和扫描 JSON 通过临时文件替换，避免半写入文件。正常采样间隔默认 60 秒，不做忙轮询。

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
