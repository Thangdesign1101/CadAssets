;;; ==========================================================================
;;; LỆNH: TA (Smart Text Align - Phiên bản nâng cấp V5)
;;; - Lệnh tắt: TA
;;; - Chế độ [1-RotateInPlace]: Giữ nguyên vị trí Text, xoay theo đường có sẵn.
;;; - Chế độ [2-AlongCurve]: Đưa Text bám dọc đường dẫn (có tùy chọn Bẻ cong chữ).
;;; - Chế độ [3-VirtualPath]: Sắp xếp các Text theo ĐÚNG THỨ TỰ CLICK / QUÉT CHỌN
;;;   dọc theo đường dẫn ảo (Start -> End) với 2 tùy chọn linh hoạt:
;;;   + [1-HangDoc]: Xếp thành cột (từng dòng chồng lên nhau), khoảng cách dòng = 1.6 * h
;;;   + [2-HangNgang]: Xếp nối đuôi nhau trên cùng 1 hàng, khoảng cách chữ = 1.6 * h
;;; ==========================================================================

(vl-load-com)

;; 1. HÀM CHỐNG NGƯỢC CHỮ (AUTO-READABLE ANGLE)
(defun Make-Readable-Angle (ang)
  (setq ang (rem ang (* 2.0 pi)))
  (if (< ang 0.0) (setq ang (+ ang (* 2.0 pi))))
  (if (and (> ang (* 0.5 pi)) (<= ang (* 1.5 pi)))
    (setq ang (- ang pi))
  )
  ang
)

;; 2. HÀM TÍNH GÓC TIẾP TUYẾN CỦA ĐƯỜNG DẪN TẠI ĐIỂM GẦN NHẤT
(defun Get-Curve-Tangent-At-Point (curve pt / closest_pt param deriv)
  (setq closest_pt (vlax-curve-getClosestPointTo curve pt)
        param      (vlax-curve-getParamAtPoint curve closest_pt)
        deriv      (vlax-curve-getFirstDeriv curve param)
  )
  (list closest_pt (angle '(0.0 0.0 0.0) deriv) param)
)

;; 3. HÀM XOAY TEXT TẠI TÂM HÌNH HỌC (GIỮ NGUYÊN VỊ TRÍ)
(defun Rotate-Text-At-Center (ent new_ang / vla_obj min_pt max_pt p1 p2 ctr old_ang)
  (setq vla_obj (vlax-ename->vla-object ent)
        old_ang (vla-get-Rotation vla_obj)
  )
  (if (not (vl-catch-all-error-p (vl-catch-all-apply 'vla-GetBoundingBox (list vla_obj 'min_pt 'max_pt))))
    (progn
      (setq p1  (vlax-safearray->list min_pt)
            p2  (vlax-safearray->list max_pt)
            ctr (list (/ (+ (car p1) (car p2)) 2.0) (/ (+ (cadr p1) (cadr p2)) 2.0) 0.0)
      )
      (vla-Rotate vla_obj (vlax-3d-point ctr) (- new_ang old_ang))
    )
    (vla-put-Rotation vla_obj new_ang)
  )
)

;; 4. HÀM ĐẶT VỊ TRÍ & GÓC XOAY CHO TEXT/MTEXT
(defun Set-Text-Pos-And-Angle (ent pt ang / elst obj_type just_code)
  (setq elst (entget ent)
        obj_type (cdr (assoc 0 elst))
  )
  (if (= obj_type "TEXT")
    (progn
      (setq just_code (+ (cdr (assoc 72 elst)) (cdr (assoc 73 elst))))
      (setq elst (subst (cons 50 ang) (assoc 50 elst) elst))
      (if (= just_code 0)
        (setq elst (subst (cons 10 pt) (assoc 10 elst) elst))
        (setq elst (subst (cons 11 pt) (assoc 11 elst) elst))
      )
      (entmod elst)
    )
    (progn
      (setq elst (subst (cons 50 ang) (assoc 50 elst) elst))
      (setq elst (subst (cons 10 pt) (assoc 10 elst) elst))
      (entmod elst)
    )
  )
)

;; 5. HÀM ĐẶT TEXT CĂN LỀ TRÁI (LEFT ALIGN) ĐỂ XẾP THẲNG HÀNG / THẲNG CỘT
(defun Set-Text-Left-Aligned (ent pt ang / elst obj_type vla_obj)
  (setq elst (entget ent)
        obj_type (cdr (assoc 0 elst))
  )
  (if (= obj_type "TEXT")
    (progn
      ;; Đưa về căn lề Left (72=0, 73=0) để mép trái thẳng tắp
      (if (assoc 72 elst) (setq elst (subst '(72 . 0) (assoc 72 elst) elst)))
      (if (assoc 73 elst) (setq elst (subst '(73 . 0) (assoc 73 elst) elst)))
      (setq elst (subst (cons 50 ang) (assoc 50 elst) elst))
      (setq elst (subst (cons 10 pt) (assoc 10 elst) elst))
      (if (assoc 11 elst) (setq elst (subst (cons 11 pt) (assoc 11 elst) elst)))
      (entmod elst)
    )
    (progn
      ;; Với MTEXT: đưa AttachmentPoint về Bottom-Left (7)
      (setq vla_obj (vlax-ename->vla-object ent))
      (vl-catch-all-apply 'vla-put-AttachmentPoint (list vla_obj 7))
      (vl-catch-all-apply 'vla-put-InsertionPoint (list vla_obj (vlax-3d-point pt)))
      (vl-catch-all-apply 'vla-put-Rotation (list vla_obj ang))
    )
  )
)

;; 6. HÀM TÍNH CHIỀU DÀI THỰC TẾ CỦA TEXT/MTEXT ĐỂ XẾP NỐI ĐUÔI THEO HÀNG NGANG
(defun Get-Text-Width (ent / elst obj_type box w vla_obj min_pt max_pt old_ang p1 p2)
  (setq elst (entget ent)
        obj_type (cdr (assoc 0 elst))
  )
  (if (= obj_type "TEXT")
    (progn
      (setq box (textbox elst))
      (if box
        (setq w (- (caadr box) (caar box)))
        (setq w (* (cdr (assoc 40 elst)) 2.0))
      )
    )
    (progn
      (setq vla_obj (vlax-ename->vla-object ent)
            old_ang (vla-get-Rotation vla_obj)
      )
      (vla-put-Rotation vla_obj 0.0)
      (if (not (vl-catch-all-error-p (vl-catch-all-apply 'vla-GetBoundingBox (list vla_obj 'min_pt 'max_pt))))
        (progn
          (setq p1 (vlax-safearray->list min_pt)
                p2 (vlax-safearray->list max_pt)
                w  (- (car p2) (car p1))
          )
        )
        (setq w (* (cdr (assoc 40 elst)) 3.0))
      )
      (vla-put-Rotation vla_obj old_ang)
    )
  )
  (if (<= w 0.0) (setq w (cdr (assoc 40 elst))))
  w
)

;; 7. HÀM LẤY TỌA ĐỘ GỐC CỦA TEXT/MTEXT
(defun Get-Text-Insertion-Point (ent / elst obj_type just_code)
  (setq elst (entget ent)
        obj_type (cdr (assoc 0 elst))
  )
  (if (= obj_type "TEXT")
    (progn
      (setq just_code (+ (cdr (assoc 72 elst)) (cdr (assoc 73 elst))))
      (if (= just_code 0)
        (cdr (assoc 10 elst))
        (cdr (assoc 11 elst))
      )
    )
    (cdr (assoc 10 elst))
  )
)

;; 8. HÀM GOM NHÓM ĐỐI TƯỢNG (GROUP) CHO CÁC KÝ TỰ ĐÃ BẺ CONG
(defun Make-Unnamed-Group (ent_list / doc groups grp arr i)
  (if (> (length ent_list) 1)
    (progn
      (setq doc    (vla-get-ActiveDocument (vlax-get-acad-object))
            groups (vla-get-Groups doc)
            grp    (vla-Add groups "*")
            arr    (vlax-make-safearray vlax-vbObject (cons 0 (1- (length ent_list))))
            i      0
      )
      (foreach e ent_list
        (vlax-safearray-put-element arr i (vlax-ename->vla-object e))
        (setq i (1+ i))
      )
      (vla-AppendItems grp arr)
    )
  )
)

;; 9. THUẬT TOÁN BẺ CONG TỪNG KÝ TỰ THEO ĐƯỜNG CONG (CURVE BENDING)
(defun Bend-Text-Along-Curve (ent curve offset_dist / elst str lay sty h col len i ch ch_ent box ch_w char_data 
                              total_len loop gr code cur_pt closest_pt base_dist param deriv raw_ang 
                              dir_sign side_sign cur_d item p_on_curve p_param p_deriv ch_ang norm_ang final_pt created_ents read_ang)
  (setq elst (entget ent)
        str  (cdr (assoc 1 elst))
        lay  (cdr (assoc 8 elst))
        sty  (if (assoc 7 elst) (cdr (assoc 7 elst)) (getvar "TEXTSTYLE"))
        h    (cdr (assoc 40 elst))
        col  (if (assoc 62 elst) (cdr (assoc 62 elst)) 256)
        len  (strlen str)
        total_len (vlax-curve-getDistAtParam curve (vlax-curve-getEndParam curve))
        char_data nil
        created_ents nil
        i 1
  )

  (while (<= i len)
    (setq ch (substr str i 1))
    (setq ch_ent (entmakex (list '(0 . "TEXT")
                                 (cons 8 lay)
                                 (cons 7 sty)
                                 (cons 62 col)
                                 (cons 10 '(0.0 0.0 0.0))
                                 (cons 11 '(0.0 0.0 0.0))
                                 (cons 40 h)
                                 (cons 1 ch)
                                 '(72 . 1)
                                 '(73 . 2)
                           )))
    (if (= ch " ")
      (setq ch_w (* h 0.45))
      (progn
        (setq box (textbox (entget ch_ent)))
        (setq ch_w (if box (- (caadr box) (caar box)) (* h 0.6)))
        (if (<= ch_w 0.0) (setq ch_w (* h 0.6)))
      )
    )
    (setq ch_w (+ ch_w (* h 0.18)))
    (setq char_data (append char_data (list (list ch_ent ch_w))))
    (setq created_ents (append created_ents (list ch_ent)))
    (setq i (1+ i))
  )

  (entdel ent)

  (setq loop T)
  (princ "\n>>> Re chuot doc theo duong cong de UON CHU va Click chot vi tri: ")
  (while loop
    (setq gr (grread T 15 0)
          code (car gr)
          cur_pt (cadr gr)
    )
    (cond
      ((= code 5)
        (setq closest_pt (vlax-curve-getClosestPointTo curve cur_pt)
              base_dist  (vlax-curve-getDistAtPoint curve closest_pt)
              param      (vlax-curve-getParamAtPoint curve closest_pt)
              deriv      (vlax-curve-getFirstDeriv curve param)
              raw_ang    (angle '(0.0 0.0 0.0) deriv)
        )
        (if (and (> raw_ang (* 0.5 pi)) (<= raw_ang (* 1.5 pi)))
          (setq dir_sign -1.0)
          (setq dir_sign 1.0)
        )
        (setq read_ang (Make-Readable-Angle raw_ang))
        (setq side_sign (if (< (sin (- (angle closest_pt cur_pt) read_ang)) 0.0) -1.0 1.0))

        (setq cur_d base_dist)
        (foreach item char_data
          (setq ch_ent (car item)
                ch_w   (cadr item)
                cur_d  (+ cur_d (* dir_sign (/ ch_w 2.0)))
          )
          (if (< cur_d 0.0) (setq cur_d 0.0))
          (if (> cur_d total_len) (setq cur_d total_len))

          (setq p_on_curve (vlax-curve-getPointAtDist curve cur_d)
                p_param    (vlax-curve-getParamAtPoint curve p_on_curve)
                p_deriv    (vlax-curve-getFirstDeriv curve p_param)
                ch_ang     (Make-Readable-Angle (angle '(0.0 0.0 0.0) p_deriv))
                norm_ang   (+ ch_ang (* side_sign 0.5 pi))
                final_pt   (polar p_on_curve norm_ang offset_dist)
          )
          (Set-Text-Pos-And-Angle ch_ent final_pt ch_ang)
          (setq cur_d (+ cur_d (* dir_sign (/ ch_w 2.0))))
        )
      )
      ((or (= code 3) (= code 11) (= code 25) (and (= code 2) (member cur_pt '(13 32))))
        (setq loop nil)
      )
    )
  )
  (Make-Unnamed-Group created_ents)
  (princ "\nDa uon cong Text doc theo duong dan va gom nhom (Group) thanh cong!")
)


;; ----------------- LỆNH CHÍNH: TA -----------------
(defun c:TA (/ mode bend_mode arr_mode ss curve_sel curve i ent pt res closest_pt raw_ang read_ang 
               offset_dist norm_ang new_pt loop gr code cur_pt 
               p_start p_end dir_ang txt_ang text_h def_space space_dist cur_dist txt_w)
  
  (vla-StartUndoMark (vla-get-ActiveDocument (vlax-get-acad-object)))

  ;; Menu 3 chế độ chọn nhanh bằng phím 1, 2, 3
  (initget "1-RotateInPlace 2-AlongCurve 3-VirtualPath 1 2 3")
  (setq mode (getkword "\nChon che do Text Align [1-RotateInPlace/2-AlongCurve/3-VirtualPath] <1-RotateInPlace>: "))
  (cond
    ((or (not mode) (= mode "1") (= mode "1-RotateInPlace")) (setq mode "1"))
    ((or (= mode "2") (= mode "2-AlongCurve")) (setq mode "2"))
    ((or (= mode "3") (= mode "3-VirtualPath")) (setq mode "3"))
  )

  (cond
    ;;; ========================================================================
    ;;; CHẾ ĐỘ 1: GIỮ NGUYÊN VỊ TRÍ CHỮ - XOAY THEO ĐƯỜNG DẪN CÓ SẴN
    ;;; ========================================================================
    ((= mode "1")
      (princ "\nQuet chon cac Text/MText can xoay tai cho theo duong co san...")
      (if (setq ss (ssget '((0 . "TEXT,MTEXT"))))
        (if (setq curve_sel (entsel "\n>>> Click chon duong dan co san (Line, Polyline, Arc, Spline, Circle): "))
          (progn
            (setq curve (car curve_sel) i 0)
            (if (vl-catch-all-error-p (vl-catch-all-apply 'vlax-curve-getStartPoint (list curve)))
              (alert "Doi tuong duoc chon khong phai la duong dan hop le!")
              (progn
                (while (< i (sslength ss))
                  (setq ent      (ssname ss i)
                        pt       (Get-Text-Insertion-Point ent)
                        res      (Get-Curve-Tangent-At-Point curve pt)
                        raw_ang  (cadr res)
                        read_ang (Make-Readable-Angle raw_ang)
                  )
                  (Rotate-Text-At-Center ent read_ang)
                  (setq i (1+ i))
                )
                (princ (strcat "\nDa xoay " (itoa (sslength ss)) " Text theo duong dan (giu nguyen vi tri)!"))
              )
            )
          )
        )
      )
    )

    ;;; ========================================================================
    ;;; CHẾ ĐỘ 2: BÁM DỌC THEO ĐƯỜNG DẪN (CÓ TÙY CHỌN BẺ CONG TỪNG KÝ TỰ)
    ;;; ========================================================================
    ((= mode "2")
      (initget "1-GiuNguyenText 2-BeCongTheoCurve 1 2")
      (setq bend_mode (getkword "\nChon kieu bam duong cong [1-GiuNguyenText/2-BeCongTheoCurve] <1-GiuNguyenText>: "))
      (if (or (not bend_mode) (= bend_mode "1") (= bend_mode "1-GiuNguyenText"))
        (setq bend_mode "1")
        (setq bend_mode "2")
      )

      (princ "\nChon Text/MText can dua len duong dan...")
      (if (setq ss (ssget '((0 . "TEXT,MTEXT"))))
        (if (setq curve_sel (entsel "\n>>> Click chon duong dan (Line, Polyline, Arc, Spline, Circle): "))
          (progn
            (setq curve (car curve_sel))
            (if (vl-catch-all-error-p (vl-catch-all-apply 'vlax-curve-getStartPoint (list curve)))
              (alert "Doi tuong duoc chon khong phai la duong dan hop le!")
              (progn
                (setq text_h (cdr (assoc 40 (entget (ssname ss 0)))))
                (setq offset_dist (getdist (strcat "\nNhap khoang cach Offset tu tim duong dan <" (rtos (* text_h 0.6) 2 1) ">: ")))
                (if (not offset_dist) (setq offset_dist (* text_h 0.6)))

                (if (= bend_mode "2")
                  (Bend-Text-Along-Curve (ssname ss 0) curve offset_dist)
                  (if (= (sslength ss) 1)
                    (progn
                      (setq ent (ssname ss 0) loop T)
                      (princ "\n>>> Re chuot doc theo duong dan va Click de chot vi tri: ")
                      (while loop
                        (setq gr (grread T 15 0)
                              code (car gr)
                              cur_pt (cadr gr)
                        )
                        (cond
                          ((= code 5)
                            (setq res        (Get-Curve-Tangent-At-Point curve cur_pt)
                                  closest_pt (car res)
                                  raw_ang    (cadr res)
                                  read_ang   (Make-Readable-Angle raw_ang)
                                  norm_ang   (if (< (sin (- (angle closest_pt cur_pt) read_ang)) 0.0)
                                               (- read_ang (* 0.5 pi))
                                               (+ read_ang (* 0.5 pi))
                                             )
                                  new_pt     (polar closest_pt norm_ang offset_dist)
                            )
                            (Set-Text-Pos-And-Angle ent new_pt read_ang)
                          )
                          ((or (= code 3) (= code 11) (= code 25) (and (= code 2) (member cur_pt '(13 32))))
                            (setq loop nil)
                          )
                        )
                      )
                      (princ "\nDa dat Text doc theo duong dan thanh cong!")
                    )
                    (progn
                      (setq i 0)
                      (while (< i (sslength ss))
                        (setq ent        (ssname ss i)
                              pt         (Get-Text-Insertion-Point ent)
                              res        (Get-Curve-Tangent-At-Point curve pt)
                              closest_pt (car res)
                              raw_ang    (cadr res)
                              read_ang   (Make-Readable-Angle raw_ang)
                              norm_ang   (if (< (sin (- (angle closest_pt pt) read_ang)) 0.0)
                                           (- read_ang (* 0.5 pi))
                                           (+ read_ang (* 0.5 pi))
                                         )
                              new_pt     (polar closest_pt norm_ang offset_dist)
                        )
                        (Set-Text-Pos-And-Angle ent new_pt read_ang)
                        (setq i (1+ i))
                      )
                      (princ (strcat "\nDa hut va can chinh " (itoa (sslength ss)) " Text bam doc theo duong dan!"))
                    )
                  )
                )
              )
            )
          )
        )
      )
    )

    ;;; ========================================================================
    ;;; CHẾ ĐỘ 3: FOLLOW THEO ĐƯỜNG DẪN ẢO (START -> END POINT)
    ;;; Sắp xếp theo đúng thứ tự Click/Quét chọn, linh hoạt Hàng Dọc hoặc Hàng Ngang
    ;;; Khoảng cách mặc định = 1.6 * Chiều cao chữ
    ;;; ========================================================================
    ((= mode "3")
      (princ "\nClick hoac Quet chon cac Text/MText theo thu tu mong muon...")
      (if (setq ss (ssget '((0 . "TEXT,MTEXT"))))
        (if (setq p_start (getpoint "\n>>> [BUOC 1] Click chon DIEM DAU (Start Point) cua duong dan ao: "))
          (if (setq p_end (getpoint p_start "\n>>> [BUOC 2] Keo con tro theo CHIEU SAP XEP toi DIEM CUOI (End Point) va Click: "))
            (progn
              ;; Hiển thị đường dẫn ảo màu xanh lá (màu 3) trên màn hình
              (grdraw p_start p_end 3 1)

              (setq dir_ang   (angle p_start p_end)
                    text_h    (cdr (assoc 40 (entget (ssname ss 0))))
                    def_space (* text_h 1.6)
              )

              ;; Tùy chọn linh hoạt: Xếp theo Hàng Dọc (xếp thành cột) hoặc Hàng Ngang (nối đuôi nhau)
              (initget "1-HangDoc 2-HangNgang 1 2")
              (setq arr_mode (getkword "\nChon kieu sap xep [1-HangDoc (Xep thanh cot)/2-HangNgang (Noi tiep)] <1-HangDoc>: "))
              (if (or (not arr_mode) (= arr_mode "1") (= arr_mode "1-HangDoc"))
                (setq arr_mode "1")
                (setq arr_mode "2")
              )

              ;; Nhập khoảng cách (mặc định = 1.6 * chiều cao chữ)
              (setq space_dist (getdist (strcat "\nNhap khoang cach giua cac chu <" (rtos def_space 2 1) ">: ")))
              (if (not space_dist) (setq space_dist def_space))

              (cond
                ;; --- TÙY CHỌN 1: XẾP THEO HÀNG DỌC (CÁC DÒNG XẾP CHỒNG THEO ĐƯỜNG ẢO, CHỮ VUÔNG GÓC ĐƯỜNG ẢO) ---
                ((= arr_mode "1")
                  ;; Góc chữ vuông góc với chiều rải dòng và tự động chống ngược chữ
                  ;; VD: Kéo từ trên xuống (-90 độ) -> chữ nằm ngang (0 độ)
                  (setq txt_ang (Make-Readable-Angle (+ dir_ang (* 0.5 pi)))
                        i       0
                  )
                  (while (< i (sslength ss))
                    (setq ent    (ssname ss i)
                          new_pt (polar p_start dir_ang (* i space_dist))
                    )
                    (Set-Text-Left-Aligned ent new_pt txt_ang)
                    (setq i (1+ i))
                  )
                  (redraw)
                  (princ (strcat "\nDa xep " (itoa (sslength ss)) " Text theo HANG DOC (khoang cach dong = " (rtos space_dist 2 1) ")!"))
                )

                ;; --- TÙY CHỌN 2: XẾP THEO HÀNG NGANG (CHỮ XOAY THEO ĐƯỜNG ẢO VÀ NỐI ĐUÔI NHAU) ---
                ((= arr_mode "2")
                  (setq i        0
                        cur_dist 0.0
                  )
                  (while (< i (sslength ss))
                    (setq ent    (ssname ss i)
                          new_pt (polar p_start dir_ang cur_dist)
                    )
                    (Set-Text-Left-Aligned ent new_pt dir_ang)
                    (setq txt_w    (Get-Text-Width ent)
                          cur_dist (+ cur_dist txt_w space_dist)
                          i        (1+ i)
                    )
                  )
                  (redraw)
                  (princ (strcat "\nDa xep noi tiep " (itoa (sslength ss)) " Text theo HANG NGANG (khoang cach = " (rtos space_dist 2 1) ")!"))
                )
              )
            )
          )
        )
      )
    )
  )

  (vla-EndUndoMark (vla-get-ActiveDocument (vlax-get-acad-object)))
  (princ)
)
(princ "\n*** Da Load thanh cong Lenh TA V5 (Smart Text Align). Go TA de chay ***")
(princ)