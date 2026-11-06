;;; ==========================================================================
;;; LỆNH: TD (Tọa độ & Bảng Excel) - Phiên bản nâng cấp V7
;;; - Style mặc định: "DB-Segoe UI", Cao: 250
;;; - Chế độ 1 (ExcelToCad):
;;;   + Chọn [Yes] tạo Boundary: Vẽ ranh đất, rải tên điểm, tự động Zoom, 
;;;     xuất bảng tọa độ 4 cột (tự thêm STT + lặp lại điểm đầu ở dòng cuối).
;;;   + Chọn [No] không tạo Boundary: Chế độ COPY BẢNG THUẦN TỪ EXCEL. 
;;;     Không rải điểm lên Model, chấp nhận mọi nội dung Chữ & Số, tự động 
;;;     nhận diện số cột và tự động căn chỉnh bề rộng từng cột (Auto-fit).
;;; - Chế độ 2 (CadToTable): Giữ nguyên tính năng Trọng tâm & Rê chuột ngoằn ngoèo V6.
;;; ==========================================================================

(vl-load-com)

;; 1. HÀM TẠO LAYER
(defun Make-Layer (lay col)
  (if (not (tblsearch "LAYER" lay))
    (entmake (list '(0 . "LAYER") '(100 . "AcDbSymbolTableRecord") 
                   '(100 . "AcDbLayerTableRecord") (cons 2 lay) 
                   '(70 . 0) (cons 62 col)))
  )
)

;; 2. HÀM TẠO TEXT STYLE "DB-Segoe UI"
(defun Make-TextStyle (stName fontFile)
  (if (not (tblsearch "STYLE" stName))
    (entmake (list '(0 . "STYLE")
                   '(100 . "AcDbSymbolTableRecord")
                   '(100 . "AcDbTextStyleTableRecord")
                   (cons 2 stName)
                   '(70 . 0)
                   '(40 . 0.0)
                   '(41 . 1.0)
                   '(50 . 0.0)
                   '(71 . 0)
                   (cons 3 fontFile)
                   '(4 . "")))
  )
)

;; 3. HÀM ĐỌC CLIPBOARD (TỪ EXCEL)
(defun Get-Clipboard-Text (/ htm res)
  (setq res nil)
  (if (setq htm (vlax-create-object "htmlfile"))
    (progn
      (setq res (vlax-invoke (vlax-get (vlax-get htm 'ParentWindow) 'ClipboardData) 'GetData "Text"))
      (vlax-release-object htm)
    )
  )
  res
)

;; 4. HÀM SPLIT CHUỖI & LÀM SẠCH KÝ TỰ XUỐNG DÒNG
(defun Split-String (str delim / pos lst)
  (while (setq pos (vl-string-search delim str))
    (setq lst (cons (substr str 1 pos) lst))
    (setq str (substr str (+ pos 1 (strlen delim))))
  )
  (reverse (cons str lst))
)

(defun Clean-Lines (raw_txt / lines clean_lst)
  ;; Chuẩn hóa \r\n hoặc \n từ Excel
  (setq lines (Split-String raw_txt "\n")
        clean_lst nil)
  (foreach l lines
    (setq l (vl-string-right-trim "\r" l))
    ;; Bỏ qua dòng trống hoàn toàn (chỉ toàn dấu Tab hoặc rỗng)
    (if (/= (vl-string-trim " \t" l) "")
      (setq clean_lst (append clean_lst (list l)))
    )
  )
  clean_lst
)

;; 5. HÀM TẠO MTEXT (Gán Style DB-Segoe UI)
(defun Create-MText (pt txt lay h justify styleName)
  (entmakex (list '(0 . "MTEXT") '(100 . "AcDbEntity") (cons 8 lay)
                  '(100 . "AcDbMText") (cons 10 pt) (cons 40 h) 
                  (cons 71 justify) (cons 7 styleName) (cons 1 txt)))
)

;; 6. HÀM TẠO LINE
(defun Create-Line (p1 p2 lay)
  (entmakex (list '(0 . "LINE") '(100 . "AcDbEntity") (cons 8 lay) 
                  (cons 10 p1) (cons 11 p2)))
)

;; 7. HÀM VẼ BẢNG ĐA NĂNG (TỰ ĐỘNG CO GIÃN SỐ CỘT & BỀ RỘNG CỘT) - NÂNG CẤP V7
(defun Draw-Dynamic-Table-And-Drag (all_rows text_h styleName init_pt / base_pt grip_pt num_cols col_widths c r val max_len w TotalW RH y_curr x_curr cell_txt ss_tbl e old_cmdecho)
  (setq base_pt (if init_pt init_pt (getvar "VIEWCTR"))
        base_pt (list (car base_pt) (cadr base_pt) 0.0)
        RH (* text_h 2.5)
        y_curr (cadr base_pt)
        ss_tbl (ssadd)
        num_cols (apply 'max (mapcar 'length all_rows))
        col_widths nil
        c 0
  )

  ;; Tính toán bề rộng tối ưu cho từng cột dựa trên độ dài chuỗi chữ (Auto-fit)
  (while (< c num_cols)
    (setq max_len 4) ; Độ rộng tối thiểu tương đương 4 ký tự
    (foreach row all_rows
      (setq val (if (< c (length row)) (nth c row) ""))
      (if (> (strlen val) max_len)
        (setq max_len (strlen val))
      )
    )
    ;; Quy đổi số ký tự ra bề rộng cột (nhân hệ số 0.95 + khoảng đệm 3.0 lần chiều cao chữ)
    (setq w (* text_h (+ (* max_len 0.95) 3.0)))
    (setq col_widths (append col_widths (list w)))
    (setq c (1+ c))
  )

  (setq TotalW (apply '+ col_widths))

  ;; Kẻ đường ngang trên cùng của bảng
  (if (setq e (Create-Line (list (car base_pt) y_curr) (list (+ (car base_pt) TotalW) y_curr) "DB-A-Text"))
    (ssadd e ss_tbl)
  )

  ;; Duyệt từng hàng và từng cột để điền MText (Chấp nhận mọi kiểu chữ và số)
  (foreach row all_rows
    (setq x_curr (car base_pt)
          c 0
    )
    (while (< c num_cols)
      (setq w (nth c col_widths)
            cell_txt (if (< c (length row)) (nth c row) "")
      )
      (if (/= cell_txt "")
        (if (setq e (Create-MText (list (+ x_curr (/ w 2.0)) (- y_curr (/ RH 2.0))) cell_txt "DB-A-Text" text_h 5 styleName))
          (ssadd e ss_tbl)
        )
      )
      (setq x_curr (+ x_curr w)
            c (1+ c)
      )
    )
    (setq y_curr (- y_curr RH))
    ;; Kẻ đường ngang dưới mỗi dòng
    (if (setq e (Create-Line (list (car base_pt) y_curr) (list (+ (car base_pt) TotalW) y_curr) "DB-A-Text"))
      (ssadd e ss_tbl)
    )
  )

  ;; Kẻ các đường dọc phân chia cột
  (setq x_curr (car base_pt))
  (if (setq e (Create-Line (list x_curr (cadr base_pt)) (list x_curr y_curr) "DB-A-Text"))
    (ssadd e ss_tbl)
  )
  (foreach w col_widths
    (setq x_curr (+ x_curr w))
    (if (setq e (Create-Line (list x_curr (cadr base_pt)) (list x_curr y_curr) "DB-A-Text"))
      (ssadd e ss_tbl)
    )
  )

  ;; Điểm cầm con trỏ chuột cố định tại GÓC DƯỚI BÊN PHẢI của bảng
  (setq grip_pt (list (+ (car base_pt) TotalW) y_curr 0.0))

  (setq old_cmdecho (getvar "CMDECHO"))
  (setvar "CMDECHO" 0)
  (princ "\n>>> Di chuot va Click chon diem dat Bang (Goc duoi ben phai): ")
  (command "_.MOVE" ss_tbl "" "_non" grip_pt PAUSE)
  (setvar "CMDECHO" old_cmdecho)
)

;; Hàm bọc cho bảng tọa độ tiêu chuẩn 4 cột (Chế độ có Boundary & Chế độ 2)
(defun Draw-Coord-Table-Standard (data text_h styleName init_pt / idx rows_with_stt)
  (setq rows_with_stt nil idx 1)
  (foreach row data
    (setq rows_with_stt (append rows_with_stt (list (cons (itoa idx) row))))
    (setq idx (1+ idx))
  )
  (setq rows_with_stt (cons '("STT" "Tên Điểm" "Tọa độ X" "Tọa độ Y") rows_with_stt))
  (Draw-Dynamic-Table-And-Drag rows_with_stt text_h styleName init_pt)
)

;; 8. HÀM TÍNH TRỌNG TÂM HÌNH HỌC (CENTROID) CHO MỌI ĐỐI TƯỢNG
(defun Get-Polygon-Area-Centroid (pts / n i p1 p2 cross sum_a sum_cx sum_cy)
  (if (and (> (length pts) 1) (equal (car pts) (last pts) 1e-4))
    (setq pts (reverse (cdr (reverse pts))))
  )
  (setq n (length pts))
  (cond
    ((= n 0) '(0.0 0.0))
    ((= n 1) (car pts))
    ((= n 2) (list (/ (+ (caar pts) (caadr pts)) 2.0) (/ (+ (cadar pts) (cadadr pts)) 2.0)))
    (T
      (setq sum_a 0.0 sum_cx 0.0 sum_cy 0.0 i 0)
      (while (< i n)
        (setq p1 (nth i pts)
              p2 (nth (rem (1+ i) n) pts)
              cross (- (* (car p1) (cadr p2)) (* (car p2) (cadr p1)))
              sum_a (+ sum_a cross)
              sum_cx (+ sum_cx (* (+ (car p1) (car p2)) cross))
              sum_cy (+ sum_cy (* (+ (cadr p1) (cadr p2)) cross))
              i (1+ i)
        )
      )
      (if (equal sum_a 0.0 1e-6)
        (list (/ (apply '+ (mapcar 'car pts)) (float n))
              (/ (apply '+ (mapcar 'cadr pts)) (float n)))
        (list (/ sum_cx (* 3.0 sum_a))
              (/ sum_cy (* 3.0 sum_a)))
      )
    )
  )
)

(defun Get-Entity-Centroid (ent / elst obj_type pts vla_obj min_pt max_pt p1 p2)
  (setq elst (entget ent)
        obj_type (cdr (assoc 0 elst))
  )
  (cond
    ((member obj_type '("POINT" "INSERT" "CIRCLE" "ELLIPSE"))
      (list (cadr (assoc 10 elst)) (caddr (assoc 10 elst)))
    )
    ((= obj_type "LWPOLYLINE")
      (setq pts (mapcar '(lambda (x) (list (cadr x) (caddr x)))
                        (vl-remove-if-not '(lambda (x) (= (car x) 10)) elst)))
      (Get-Polygon-Area-Centroid pts)
    )
    (T
      (setq vla_obj (vlax-ename->vla-object ent))
      (if (not (vl-catch-all-error-p (vl-catch-all-apply 'vla-GetBoundingBox (list vla_obj 'min_pt 'max_pt))))
        (progn
          (setq p1 (vlax-safearray->list min_pt)
                p2 (vlax-safearray->list max_pt))
          (list (/ (+ (car p1) (car p2)) 2.0) (/ (+ (cadr p1) (cadr p2)) 2.0))
        )
        '(0.0 0.0)
      )
    )
  )
)

;; 9. THUẬT TOÁN VẼ ĐƯỜNG NGOẰN NGOÈO (FREEHAND PATH) VÀ SẮP XẾP ĐIỂM
(defun Capture-Freehand-Path ( / p1 path last_pt loop gr code pt min_step)
  (setq p1 (getpoint "\n>>> [BUOC 1] Click chon DIEM BAT DAU va RE CHUOT NGOAN NGOEO qua cac diem <Enter = Giu nguyen thu tu>: "))
  (if p1
    (progn
      (setq path (list (list (car p1) (cadr p1)))
            last_pt p1
            loop T
            min_step (/ (getvar "VIEWSIZE") 150.0)
      )
      (princ "\n>>> [BUOC 2] Dang re chuot ve duong dan... (Click chuot trai, chuot phai hoac Enter de KET THUC): ")
      (while loop
        (setq gr (grread T 15 0)
              code (car gr)
              pt (cadr gr)
        )
        (cond
          ((= code 5)
            (if (> (distance last_pt pt) min_step)
              (progn
                (grdraw last_pt pt 3 1)
                (setq path (append path (list (list (car pt) (cadr pt))))
                      last_pt pt
                )
              )
            )
          )
          ((or (= code 3) (= code 11) (= code 25) (and (= code 2) (member pt '(13 32))))
            (if (and (= code 3) (listp pt))
              (progn
                (grdraw last_pt pt 3 1)
                (setq path (append path (list (list (car pt) (cadr pt)))))
              )
            )
            (setq loop nil)
          )
        )
      )
      (redraw)
      path
    )
    nil
  )
)

(defun Get-Position-Along-Path (pt path / total_len best_pos min_dist p1 p2 vx vy wx wy c1 c2 b pb d seg_len)
  (setq total_len 0.0
        best_pos 0.0
        min_dist 1e99
  )
  (while (> (length path) 1)
    (setq p1 (car path)
          p2 (cadr path)
          vx (- (car p2) (car p1))
          vy (- (cadr p2) (cadr p1))
          wx (- (car pt) (car p1))
          wy (- (cadr pt) (cadr p1))
          c1 (+ (* wx vx) (* wy vy))
          c2 (+ (* vx vx) (* vy vy))
          seg_len (distance p1 p2)
    )
    (cond
      ((<= c1 0.0) (setq b 0.0 pb p1))
      ((<= c2 c1)  (setq b 1.0 pb p2))
      (T
        (setq b (/ c1 c2)
              pb (list (+ (car p1) (* b vx)) (+ (cadr p1) (* b vy)))
        )
      )
    )
    (setq d (distance pt pb))
    (if (< d min_dist)
      (setq min_dist d
            best_pos (+ total_len (* b seg_len))
      )
    )
    (setq total_len (+ total_len seg_len)
          path (cdr path)
    )
  )
  best_pos
)

(defun Sort-Points-By-Freehand-Path (pts path / paired)
  (if (and path (> (length path) 1) (> (length pts) 1))
    (progn
      (setq paired (mapcar '(lambda (p) (cons (Get-Position-Along-Path p path) p)) pts))
      (mapcar 'cdr (vl-sort paired '(lambda (a b) (< (car a) (car b)))))
    )
    pts
  )
)

;; 10. HÀM AUTO-ZOOM ĐƯA ĐỐI TƯỢNG VỀ TRUNG TÂM MÀN HÌNH
(defun Zoom-To-Points (pt_list text_h / xs ys min_x max_x min_y max_y dx dy pad p_min p_max center_pt)
  (if (> (length pt_list) 0)
    (progn
      (setq xs (mapcar 'car pt_list)
            ys (mapcar 'cadr pt_list)
            min_x (apply 'min xs)
            max_x (apply 'max xs)
            min_y (apply 'min ys)
            max_y (apply 'max ys)
            dx (- max_x min_x)
            dy (- max_y min_y)
            pad (max (* (max dx dy) 0.25) (* text_h 25.0))
            p_min (list (- min_x pad) (- min_y pad) 0.0)
            p_max (list (+ max_x pad) (+ max_y pad) 0.0)
            center_pt (list (/ (+ min_x max_x) 2.0) (/ (+ min_y max_y) 2.0) 0.0)
      )
      (vla-ZoomWindow (vlax-get-acad-object) (vlax-3d-point p_min) (vlax-3d-point p_max))
      center_pt
    )
    (getvar "VIEWCTR")
  )
)


;; ----------------- LỆNH CHÍNH: TD -----------------
(defun c:TD (/ styleName mode pt_mode text_h draw_bound add_stt raw_txt rows cols data table_data raw_table idx pt_list first_pt last_pt view_ctr ss i ent elst obj_type pts path prefix name X Y)
  (vla-StartUndoMark (vla-get-ActiveDocument (vlax-get-acad-object)))
  
  ;; 1. Khởi tạo Layer & Text Style chuẩn
  (setq styleName "DB-Segoe UI")
  (Make-Layer "DB-A-Text" 2)          ; Layer text (Màu Vàng)
  (Make-Layer "DB-L-Site Boundary" 1) ; Layer ranh (Màu Đỏ)
  (Make-TextStyle styleName "segoeui.ttf")

  ;; 2. Chọn chế độ bằng phím tắt 1 hoặc 2
  (initget "1-ExcelToCad 2-CadToTable 1 2")
  (setq mode (getkword "\nChon che do [1-ExcelToCad/2-CadToTable] <1-ExcelToCad>: "))
  (if (or (not mode) (= mode "1") (= mode "1-ExcelToCad"))
    (setq mode "1")
    (setq mode "2")
  )
  
  ;; 3. Chiều cao chữ mặc định 250
  (setq text_h (getreal "\nNhap chieu cao chu <250.0>: "))
  (if (not text_h) (setq text_h 250.0))
  (setvar "TEXTSIZE" text_h)

  (cond
    ;;; ================= CHẾ ĐỘ 1: EXCEL -> CAD =================
    ((= mode "1")
      (setq raw_txt (Get-Clipboard-Text))
      (if (or (not raw_txt) (= (vl-string-trim " \t\r\n" raw_txt) ""))
        (alert "Clipboard dang trong! Hay Ctrl+C bang du lieu trong Excel truoc.")
        (progn
          (initget "Yes No")
          (setq draw_bound (getkword "\nCo tao duong bao (Site Boundary) va rai diem tren Model khong? [Yes/No] <Yes>: "))
          (if (not draw_bound) (setq draw_bound "Yes"))

          (setq rows (Clean-Lines raw_txt))

          (if (= draw_bound "No")
            ;;; --- NHÁNH 1A: CHẾ ĐỘ COPY BẢNG THUẦN TỪ EXCEL (CHẤP NHẬN MỌI CHỮ & SỐ) ---
            (progn
              (setq raw_table (mapcar '(lambda (r) (Split-String r "\t")) rows))
              (if (> (length raw_table) 0)
                (progn
                  (initget "Yes No")
                  (setq add_stt (getkword "\nCo tu dong danh them cot STT vao dau bang khong? [Yes/No] <No>: "))
                  (if (= add_stt "Yes")
                    (progn
                      (setq idx 1 table_data nil)
                      (foreach r raw_table
                        (setq table_data (append table_data (list (cons (itoa idx) r))))
                        (setq idx (1+ idx))
                      )
                      ;; Nếu bảng có 3 cột gốc (cộng cột STT là 4), tự thêm dòng Header chuẩn
                      (if (= (length (car raw_table)) 3)
                        (setq table_data (cons '("STT" "Tên Điểm" "Tọa độ X" "Tọa độ Y") table_data))
                      )
                    )
                    (setq table_data raw_table)
                  )

                  ;; Chỉ vẽ mỗi bảng ngay tại tâm màn hình hiện tại, KHÔNG rải chữ lên Model
                  (Draw-Dynamic-Table-And-Drag table_data text_h styleName (getvar "VIEWCTR"))
                  (princ "\nDa paste bang Excel thuan tuy vao CAD thanh cong!")
                )
                (alert "Khong tim thay du lieu trong Clipboard!")
              )
            )

            ;;; --- NHÁNH 1B: CHẾ ĐỘ TỌA ĐỘ (VẼ BOUNDARY, RẢI ĐIỂM MODEL, ZOOM & XUẤT BẢNG) ---
            (progn
              (setq data nil pt_list nil)
              (foreach r rows
                (setq cols (Split-String r "\t"))
                (if (and (>= (length cols) 3) (distof (nth 1 cols)) (distof (nth 2 cols)))
                  (progn
                    (setq data (append data (list (list (nth 0 cols) (nth 1 cols) (nth 2 cols)))))
                    (setq pt_list (append pt_list (list (list (atof (nth 1 cols)) (atof (nth 2 cols))))))
                  )
                )
              )
              
              (if (> (length data) 0)
                (progn
                  ;; Vẽ Polyline ranh giới
                  (if (> (length pt_list) 1)
                    (entmakex (append (list '(0 . "LWPOLYLINE") '(100 . "AcDbEntity") (cons 8 "DB-L-Site Boundary") 
                                            '(100 . "AcDbPolyline") (cons 90 (length pt_list)) '(70 . 1))
                                      (mapcar '(lambda (pt) (cons 10 pt)) pt_list)))
                  )
                  
                  ;; Rải tên điểm lên Model
                  (setq i 0)
                  (foreach pt pt_list
                    (if (not (and (> i 0) (= i (1- (length pt_list))) (equal pt (car pt_list) 1e-4)))
                      (Create-MText (list (+ (car pt) (* text_h 0.5)) (+ (cadr pt) (* text_h 0.5))) 
                                    (car (nth i data)) "DB-A-Text" text_h 7 styleName)
                    )
                    (setq i (1+ i))
                  )

                  ;; Lặp lại điểm đầu ở dòng cuối bảng
                  (setq table_data data)
                  (if (> (length pt_list) 1)
                    (progn
                      (setq first_pt (car pt_list)
                            last_pt  (last pt_list))
                      (if (not (equal first_pt last_pt 1e-4))
                        (setq table_data (append table_data (list (car data))))
                      )
                    )
                  )

                  ;; Auto-Zoom tới khu đất và đặt bảng
                  (setq view_ctr (Zoom-To-Points pt_list text_h))
                  (Draw-Coord-Table-Standard table_data text_h styleName view_ctr)
                  (princ "\nDa tao Boundary, rai diem, Zoom toi khu dat va dat bang thanh cong!")
                )
                (alert "Khong tim thay toa do so X, Y hop le!\nNeu ban muon paste bang chu/so thuan tuy tu Excel, hay chon [No] o cau hoi tao duong bao.")
              )
            )
          )
        )
      )
    )

    ;;; ================= CHẾ ĐỘ 2: CAD -> TABLE =================
    ((= mode "2")
      (initget "1-TrongTam 2-DinhPolyline 1 2")
      (setq pt_mode (getkword "\nChon kieu lay diem [1-TrongTam/2-DinhPolyline] <1-TrongTam>: "))
      (if (or (not pt_mode) (= pt_mode "1") (= pt_mode "1-TrongTam"))
        (setq pt_mode "1")
        (setq pt_mode "2")
      )

      (princ "\nQuet chon cac doi tuong (Hinh chu nhat, Hinh vuong, Polyline, Elip, Circle, Block, Point)...")
      (if (setq ss (ssget '((0 . "LWPOLYLINE,POINT,INSERT,CIRCLE,ELLIPSE"))))
        (progn
          (setq i 0 pts nil data nil pt_list nil)
          
          (while (< i (sslength ss))
            (setq ent (ssname ss i) 
                  elst (entget ent)
                  obj_type (cdr (assoc 0 elst))
            )
            (if (= pt_mode "1")
              (setq pts (append pts (list (Get-Entity-Centroid ent))))
              (if (= obj_type "LWPOLYLINE")
                (setq pts (append pts (mapcar '(lambda (x) (list (cadr x) (caddr x))) 
                                              (vl-remove-if-not '(lambda (x) (= (car x) 10)) elst))))
                (setq pts (append pts (list (Get-Entity-Centroid ent))))
              )
            )
            (setq i (1+ i))
          )

          (if (and (= pt_mode "2") (> (length pts) 1) (equal (car pts) (last pts) 1e-4))
            (setq pts (reverse (cdr (reverse pts))))
          )

          ;; Vẽ đường dẫn ngoằn ngoèo tự do để sắp xếp thứ tự điểm
          (if (> (length pts) 1)
            (progn
              (setq path (Capture-Freehand-Path))
              (if (and path (> (length path) 1))
                (setq pts (Sort-Points-By-Freehand-Path pts path))
              )
            )
          )

          (setq prefix (getstring "\nNhap tien to cho cac diem (VD: A, B...) <A>: "))
          (if (= prefix "") (setq prefix "A"))

          (initget "Yes No")
          (setq draw_bound (getkword "\nCo tao duong bao (Site Boundary) noi cac trong tam/diem khong? [Yes/No] <Yes>: "))
          (if (not draw_bound) (setq draw_bound "Yes"))
          
          (setq i 1)
          (foreach pt pts
            (setq name (strcat prefix (itoa i)))
            (setq X (rtos (car pt) 2 3) Y (rtos (cadr pt) 2 3))
            (setq data (append data (list (list name X Y))))
            (setq pt_list (append pt_list (list (list (car pt) (cadr pt)))))
            
            (Create-MText (list (+ (car pt) (* text_h 0.5)) (+ (cadr pt) (* text_h 0.5))) 
                          name "DB-A-Text" text_h 7 styleName)
            (setq i (1+ i))
          )

          (if (and (= draw_bound "Yes") (> (length pt_list) 1))
            (entmakex (append (list '(0 . "LWPOLYLINE") '(100 . "AcDbEntity") (cons 8 "DB-L-Site Boundary") 
                                    '(100 . "AcDbPolyline") (cons 90 (length pt_list)) '(70 . 1))
                              (mapcar '(lambda (p) (cons 10 p)) pt_list)))
          )

          (setq table_data data)
          (if (> (length data) 1)
            (setq table_data (append table_data (list (car data))))
          )

          (setq view_ctr (Zoom-To-Points pt_list text_h))
          (Draw-Coord-Table-Standard table_data text_h styleName view_ctr)
          (princ "\nDa xuat bang toa do Trong Tam theo duong re chuot ngoan ngoeo thanh cong!")
        )
        (princ "\nKhong co doi tuong hop le nao duoc chon.")
      )
    )
  )
  
  (vla-EndUndoMark (vla-get-ActiveDocument (vlax-get-acad-object)))
  (princ)
)
(princ "\n*** Da Load thanh cong TD V7. Go lenh TD de chay script ***")
(princ)