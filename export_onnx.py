from ultralytics import YOLO

# Thay đường dẫn này thành đường dẫn file .pt của bạn
model = YOLO(r"C:\Users\khang\OneDrive\Documents\KTPM_DO-AN\NST_AI_Project\models\best.pt")

# Xuất sang ONNX
model.export(
    format="onnx",
    imgsz=640,      # kích thước ảnh input, thường là 640
    opset=11,       # phiên bản ONNX, 11 là ổn định nhất
    simplify=True,  # tối ưu model
)

print("Xuất thành công!")
print("File .onnx nằm cùng thư mục với file .pt")